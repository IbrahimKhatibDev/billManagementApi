using System.Net;
using System.Text.Json;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The probes, exercised through the anonymous client on purpose.
/// <para>
/// Program.cs sets a fallback authorization policy that closes every endpoint
/// which does not open itself, so the interesting question about a health check
/// in this app is not "does it report the right status" but "does it answer at
/// all to a caller with no token". Docker's healthcheck has no token. If the
/// <c>AllowAnonymous</c> in HealthEndpoints is ever dropped, these probes start
/// returning 401, the api container never becomes healthy, and the blazor
/// service that waits on it never starts — a whole-stack outage from one
/// deleted line.
/// </para>
/// </summary>
public sealed class HealthCheckTests : ApiTestBase
{
    public HealthCheckTests(PostgresApiFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_answer_a_caller_with_no_token(string url)
    {
        var response = await Fixture.AnonymousClient.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_the_database_check()
    {
        var response = await Fixture.AnonymousClient.GetAsync("/health/ready");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());

        // The named check, not just the aggregate: an empty predicate would also
        // report "Healthy" while checking nothing at all, which is the way this
        // endpoint is most likely to quietly stop being useful.
        var names = root.GetProperty("checks")
            .EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("postgres", names);
    }

    [Fact]
    public async Task Liveness_runs_no_checks()
    {
        var response = await Fixture.AnonymousClient.GetAsync("/health/live");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Deliberately empty: liveness must not depend on Postgres, or a database
        // blip would get every API instance killed and restarted at once. This
        // asserts the separation rather than trusting the comment in
        // HealthEndpoints to survive a future edit.
        Assert.Empty(document.RootElement.GetProperty("checks").EnumerateArray());
    }
}
