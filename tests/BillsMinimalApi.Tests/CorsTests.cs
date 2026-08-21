using System.Net;
using BillsMinimalApi.Logging;
using Microsoft.Net.Http.Headers;

namespace BillsMinimalApi.Tests;

/// <summary>
/// What a browser is allowed to do with a response from this API.
/// <para>
/// None of this is exercised by the Blazor app in this repo, which is Blazor
/// Server and calls the API from inside a container rather than from a page. It
/// is load-bearing for every browser client that is not in this repo, and
/// invisible to every other caller: `curl` and Swagger see these headers whether
/// the policy names them or not, which is exactly why the omission this class
/// guards against is the sort that ships.
/// </para>
/// </summary>
public sealed class CorsTests : ApiTestBase
{
    /// <summary>Any origin that is not the API's own; the policy allows all of them.</summary>
    private const string Origin = "http://localhost:5173";

    public CorsTests(PostgresApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Both_headers_this_api_adds_are_readable_from_another_origin()
    {
        // A browser hands script only a short safelist of response headers unless
        // the server names the rest. Neither of these is on it, so without
        // WithExposedHeaders the 429's Retry-After and the correlation id are on
        // the wire, visible in DevTools, and unreachable from fetch() — a client
        // that cannot say when to retry or quote an id into a bug report.
        using var response = await GetWithOriginAsync("/health/live");

        Assert.True(
            response.Headers.TryGetValues(HeaderNames.AccessControlExposeHeaders, out var values),
            "The policy exposes no response headers at all.");

        // Joined rather than compared element by element: the middleware writes
        // one comma-separated header and the client is free to hand it back
        // either way.
        var exposed = string.Join(",", values!);

        Assert.Contains(HeaderNames.RetryAfter, exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CorrelationId.HeaderName, exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_preflight_is_answered_rather_than_challenged()
    {
        // The reason UseCors sits ahead of UseAuthentication. A preflight carries
        // no Authorization header — the spec forbids it — so once the fallback
        // policy gets to it the answer is 401, and the browser refuses the real
        // request that was going to carry a perfectly good token. Reordering that
        // pair breaks browser clients and nothing else — so nothing in this repo
        // would notice, which is a bad way to find out.
        using var request = new HttpRequestMessage(HttpMethod.Options, Routes.Bills);
        request.Headers.Add(HeaderNames.Origin, Origin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, "GET");

        using var response = await Fixture.AnonymousClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetWithOriginAsync(string url)
    {
        // The Origin header is what makes this a cross-origin request as far as
        // the middleware is concerned; without it CORS adds nothing to the
        // response and the assertions below would be testing nothing.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(HeaderNames.Origin, Origin);

        return await Fixture.AnonymousClient.SendAsync(request);
    }
}
