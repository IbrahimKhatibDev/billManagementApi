using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The canary for the whole UTC decision. Npgsql maps <see cref="DateTime"/> to
/// <c>timestamp with time zone</c> and throws outright on
/// <see cref="DateTimeKind.Unspecified"/>, which is exactly what a bare
/// <c>"2026-03-15"</c> in a JSON body deserialises to. These tests post raw JSON
/// rather than a serialised DTO, because a DTO round-trip would hide the very
/// thing under test: what Kind the value arrives with.
/// </summary>
public class UtcRoundTripTests : ApiTestBase
{
    public UtcRoundTripTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Theory]
    // A date with no time and no offset — the Blazor date input and the .http
    // samples both send this shape. Deserialises as Kind=Unspecified.
    [InlineData("2026-03-15", "2026-03-15T00:00:00Z")]
    // Already explicit UTC: must survive untouched.
    [InlineData("2026-03-15T08:30:00Z", "2026-03-15T08:30:00Z")]
    public async Task Post_normalises_the_due_date_to_UTC_and_it_round_trips(
        string sent,
        string expected)
    {
        var json = $$"""
            {
              "id": 0,
              "payeeName": "UTC Probe",
              "dueDate": "{{sent}}",
              "paymentDue": 42.50,
              "paid": false,
              "version": 0
            }
            """;

        var response = await Client.PostAsync(
            Routes.Bills,
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadBodyAsync(response);

        // The value the POST echoes back and the value a later GET returns have
        // to be the same string. They were not before UtcDateTime.Normalize
        // moved into the mappers: the converter fixed what was stored while
        // leaving the in-memory entity alone, so POST answered
        // "2026-03-15T00:00:00" and GET answered "2026-03-15T00:00:00Z".
        Assert.Equal(expected, created.GetProperty("dueDate").GetString());

        var id = created.GetProperty("id").GetInt64();
        var fetched = await ReadBodyAsync(await Client.GetAsync($"{Routes.Bills}/{id}"));

        Assert.Equal(expected, fetched.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Put_normalises_the_due_date_to_UTC_as_well()
    {
        var created = await Fixture.CreateBillAsync();

        var json = $$"""
            {
              "id": {{created.Id}},
              "payeeName": "UTC Probe",
              "dueDate": "2026-07-04",
              "paymentDue": 42.50,
              "paid": false,
              "version": {{created.Version}}
            }
            """;

        var response = await Client.PutAsync(
            $"{Routes.Bills}/{created.Id}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-07-04T00:00:00Z", await ReadDueDateAsync(response));
    }

    /// <summary>
    /// The seeder runs on host startup, before any test body, and Bogus produces
    /// Kind=Local dates. If the normalisation regressed, the fixture would never
    /// have come up — but assert on the stored Kind anyway so the failure names
    /// itself instead of arriving as a container timeout.
    /// </summary>
    [Fact]
    public async Task Stored_timestamps_come_back_as_Utc()
    {
        var created = await Fixture.CreateBillAsync();
        var entity = await Fixture.ReadEntityAsync(created.Id);

        Assert.Equal(DateTimeKind.Utc, entity!.DueDate.Kind);
        Assert.Equal(DateTimeKind.Utc, entity.CreateTime.Kind);
    }

    /// <summary>
    /// Buffers the body into a <see cref="JsonElement"/>. Read it once and keep
    /// the result — <c>HttpContent</c> is a one-shot stream here, so a second
    /// <c>ReadFromJsonAsync</c> on the same response throws
    /// <see cref="ObjectDisposedException"/> rather than returning the body again.
    /// </summary>
    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ReadDueDateAsync(HttpResponseMessage response) =>
        (await ReadBodyAsync(response)).GetProperty("dueDate").GetString()!;
}
