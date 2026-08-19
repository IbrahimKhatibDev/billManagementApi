using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The rate limiter, against a host of this class's own with limits small enough
/// to reach in a test.
/// <para>
/// Its own host rather than the shared fixture's because the two cannot coexist.
/// Under TestServer there is no socket, so <c>Connection.RemoteIpAddress</c> is
/// null and the limiter puts every request in one partition — meaning a budget
/// small enough to exhaust deliberately here would also be exhausted accidentally
/// by the rest of the suite. <see cref="PostgresApiFixture"/> therefore raises
/// the limits out of the way for everyone else, and this class puts them back.
/// </para>
/// <para>
/// It still joins the collection: the fixture owns the database container and
/// sets the connection string this host reads, and the collection is what stops
/// another class from building a factory while these environment variables are
/// changed.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitTests : IAsyncLifetime
{
    /// <summary>
    /// Small, but not so small that the sliding window's segments round it away:
    /// the limiter divides the permit budget across six segments, and single
    /// digits divide cleanly enough to keep the arithmetic in the assertions
    /// obvious.
    /// </summary>
    private const int AuthLimit = 3;

    private const int GlobalLimit = 8;

    private readonly PostgresApiFixture _fixture;

    private WebApplicationFactory<Program>? _factory;

    private HttpClient _client = default!;

    public RateLimitTests(PostgresApiFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        // Per test, not per class — xUnit builds a fresh instance of this class
        // for each one, which is exactly what is wanted here: the limiter's
        // counters live in the host, so each test starts with a full budget
        // instead of inheriting whatever the previous one spent.
        SetLimits(AuthLimit.ToString(CultureInfo.InvariantCulture), GlobalLimit.ToString(CultureInfo.InvariantCulture));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        // Put the fixture's own values back before the next class runs. Leaving
        // these set would hand the rest of the suite a limit it exhausts in the
        // first second, and the failures would land in whichever tests happened
        // to run next.
        SetLimits("1000000", "1000000");
    }

    private static void SetLimits(string auth, string global)
    {
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", auth);
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", global);
    }

    [Fact]
    public async Task Sign_in_attempts_past_the_limit_are_refused()
    {
        // Wrong password on purpose: the limit is about how many attempts are
        // allowed, not how many succeed, and it is the failing ones that matter.
        for (var i = 0; i < AuthLimit; i++)
        {
            var allowed = await Login("nobody@tests.local", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var refused = await Login("nobody@tests.local", "wrong-password");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task A_refusal_says_how_long_to_wait()
    {
        for (var i = 0; i < AuthLimit; i++)
        {
            await Login("nobody@tests.local", "wrong-password");
        }

        var refused = await Login("nobody@tests.local", "wrong-password");

        // Worth a test of its own rather than an extra assertion on the one
        // above, because this header is the part that does not come for free:
        // SlidingWindowRateLimiter never populates the metadata it advertises, so
        // without the fallback in RateLimitSetup this 429 goes out bare and
        // nothing else in the suite would notice.
        var retryAfter = refused.Headers.RetryAfter;

        Assert.NotNull(retryAfter);
        Assert.NotNull(retryAfter.Delta);
        Assert.InRange(retryAfter.Delta.Value.TotalSeconds, 1, 60);
    }

    [Fact]
    public async Task A_refusal_is_a_problem_document_like_every_other_error()
    {
        for (var i = 0; i < AuthLimit; i++)
        {
            await Login("nobody@tests.local", "wrong-password");
        }

        var refused = await Login("nobody@tests.local", "wrong-password");

        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);

        var problem = await refused.Content.ReadFromJsonAsync<ProblemShape>();

        Assert.Equal(429, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Fact]
    public async Task The_general_limit_applies_before_anything_checks_the_token()
    {
        // No Authorization header, so every one of these would be a 401 on its
        // own. That the last is a 429 instead is the assertion: the limiter sits
        // ahead of authentication, which is what stops a flood from costing a
        // signature validation per request.
        for (var i = 0; i < GlobalLimit; i++)
        {
            using var allowed = await _client.GetAsync(Routes.Bills);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var refused = await _client.GetAsync(Routes.Bills);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task The_health_probes_are_never_refused()
    {
        // The one exemption that has to hold. Docker polls /health/live forever;
        // if those polls spent the same budget as everything else, the traffic
        // spike a rate limiter exists to absorb would make the probe fail and get
        // the container restarted — turning a slow ten minutes into a restart
        // loop. Well past the global limit on purpose.
        for (var i = 0; i < GlobalLimit * 3; i++)
        {
            using var response = await _client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });

    /// <summary>
    /// Enough of RFC 7807 to assert on. The API returns problem documents from
    /// several places and has no type of its own to deserialize them into.
    /// </summary>
    private sealed record ProblemShape(string? Title, string? Detail, int? Status);
}
