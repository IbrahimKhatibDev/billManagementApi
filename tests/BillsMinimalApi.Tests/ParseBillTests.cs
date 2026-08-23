using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The parse endpoint reads a line and writes nothing. The grammar itself is
/// covered exhaustively in the unit suite; what matters here is that the route
/// exists, is closed like every other, and leaves the table alone.
/// </summary>
public sealed class ParseBillTests(PostgresApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_line_of_text_comes_back_read()
    {
        // "today" rather than "fri", because the server resolves against its own
        // clock and only "today" is a date this test can name.
        var response = await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        response.EnsureSuccessStatusCode();
        var parsed = (await response.Content.ReadFromJsonAsync<ParsedBill>())!;

        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(89.20m, parsed.Amount);
        Assert.Equal(DateTime.UtcNow.Date, parsed.DueDate);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public async Task Reading_a_bill_does_not_create_one()
    {
        // The whole point of the endpoint: the user gets to correct the reading
        // before anything is committed.
        var response = await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        response.EnsureSuccessStatusCode();

        var page = await Fixture.GetPageAsync();

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_line_the_grammar_cannot_place_is_a_low_reading_and_not_an_error()
    {
        var response = await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "pay the gas people" });

        response.EnsureSuccessStatusCode();
        var parsed = (await response.Content.ReadFromJsonAsync<ParsedBill>())!;

        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
        Assert.Null(parsed.Amount);
    }

    [Fact]
    public async Task Parsing_is_closed_to_anonymous_callers_like_everything_else()
    {
        var response = await Fixture.AnonymousClient.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
