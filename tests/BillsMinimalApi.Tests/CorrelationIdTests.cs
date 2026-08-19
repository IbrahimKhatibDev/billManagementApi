namespace BillsMinimalApi.Tests;

/// <summary>
/// The correlation id middleware, exercised through /health/live because it is
/// the cheapest endpoint that reaches the pipeline without a token.
/// </summary>
public sealed class CorrelationIdTests : ApiTestBase
{
    private const string Header = "X-Correlation-ID";

    public CorrelationIdTests(PostgresApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Every_response_carries_an_id_even_when_none_was_sent()
    {
        var response = await Fixture.AnonymousClient.GetAsync("/health/live");

        Assert.True(response.Headers.TryGetValues(Header, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }

    [Fact]
    public async Task A_well_formed_inbound_id_is_reused()
    {
        // The whole point of accepting one: a caller that already has an id for
        // the operation gets the API's log lines filed under the same id rather
        // than under a second one nothing joins to.
        const string sent = "0af7651916cd43dd8448eb211c80319c";

        Assert.Equal(sent, await RoundTrip(sent));
    }

    [Fact]
    public async Task An_id_this_app_generated_is_one_it_accepts_back()
    {
        // The generated id is ASP.NET's trace identifier, "{connection}:{request}",
        // and the colon is exactly the kind of character an allowlist quietly
        // omits. Taking a real id from a real response rather than hard-coding
        // one keeps this honest if the fallback ever changes shape.
        using var first = await Fixture.AnonymousClient.GetAsync("/health/live");
        var generated = first.Headers.GetValues(Header).Single();

        Assert.Equal(generated, await RoundTrip(generated));
    }

    [Theory]
    // Log injection. A bare newline in a plain-text sink ends the current line
    // and starts one the caller wrote, which is how a log stops being evidence.
    [InlineData("abc\nWARN Someone else did it")]
    [InlineData("abc\r\n2026-01-01 [ERR] forged")]
    // Not a log-injection vector, but nothing legitimate needs them either, and
    // an allowlist that only bans the character of the day ages badly.
    [InlineData("id with spaces")]
    [InlineData("<script>alert(1)</script>")]
    public async Task A_hostile_inbound_id_is_dropped(string sent)
    {
        var returned = await RoundTrip(sent);

        Assert.NotEqual(sent, returned);
        Assert.DoesNotContain('\n', returned);
        Assert.DoesNotContain('\r', returned);
    }

    [Fact]
    public async Task An_over_long_inbound_id_is_dropped()
    {
        // Unbounded, an id is a free write-amplifier: one header repeated on
        // every line the request produces, paid for by whoever stores the logs.
        var sent = new string('a', 65);

        Assert.NotEqual(sent, await RoundTrip(sent));
    }

    /// <summary>
    /// Sends <paramref name="id"/> as the correlation header and returns whatever
    /// came back. TryAddWithoutValidation because HttpClient refuses to send the
    /// malformed values these tests are about.
    /// </summary>
    private async Task<string> RoundTrip(string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(Header, id);

        using var response = await Fixture.AnonymousClient.SendAsync(request);

        return response.Headers.GetValues(Header).Single();
    }
}
