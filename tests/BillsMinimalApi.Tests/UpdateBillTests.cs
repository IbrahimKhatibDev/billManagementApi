using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

public class UpdateBillTests : ApiTestBase
{
    public UpdateBillTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Put_updates_the_bill_increments_Version_and_stamps_UpdateTime()
    {
        var created = await Fixture.CreateBillAsync("Acme Corp", paymentDue: 100m);
        var before = DateTime.UtcNow.AddSeconds(-5);

        created.PayeeName = "Acme Corporation";
        created.PaymentDue = 175.25m;
        created.Paid = true;

        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/{created.Id}", created);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<BillDto>();

        Assert.Equal("Acme Corporation", updated!.PayeeName);
        Assert.Equal(175.25m, updated.PaymentDue);
        Assert.True(updated.Paid);
        Assert.Equal(2, updated.Version);

        var entity = await Fixture.ReadEntityAsync(created.Id);

        Assert.Equal(2, entity!.Version);
        Assert.NotNull(entity.UpdateTime);
        Assert.InRange(entity.UpdateTime!.Value, before, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Put_returns_400_when_the_route_id_and_the_body_id_disagree()
    {
        var created = await Fixture.CreateBillAsync();

        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/{created.Id + 1}", created);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ID mismatch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_id()
    {
        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/999999", new BillDto
        {
            Id = 999999,
            PayeeName = "Ghost",
            DueDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentDue = 10m,
            Version = 1,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The one that proves the concurrency token is wired up at all. Before the
    /// fix, the endpoint assigned <c>existing.Version</c> and left EF to build
    /// the UPDATE's WHERE clause from the value FindAsync had loaded microseconds
    /// earlier — so it always matched, DbUpdateConcurrencyException could never
    /// be thrown, and this second PUT would happily overwrite the first writer.
    /// </summary>
    [Fact]
    public async Task Put_with_a_stale_Version_returns_409()
    {
        var created = await Fixture.CreateBillAsync("Acme Corp", paymentDue: 100m);

        // First writer wins the race and moves the bill to version 2.
        var winner = await Client.PutAsJsonAsync(
            $"{Routes.Bills}/{created.Id}",
            new BillDto
            {
                Id = created.Id,
                PayeeName = "First Writer",
                DueDate = created.DueDate,
                PaymentDue = 111m,
                Version = created.Version,
            });

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        // Second writer is still holding the copy it read before that, so it is
        // sending version 1 for a row that is now at version 2.
        created.PayeeName = "Second Writer";
        var loser = await Client.PutAsJsonAsync($"{Routes.Bills}/{created.Id}", created);

        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);

        // The losing write must not have landed.
        var stored = await Client.GetFromJsonAsync<BillDto>($"{Routes.Bills}/{created.Id}");

        Assert.Equal("First Writer", stored!.PayeeName);
        Assert.Equal(111m, stored.PaymentDue);
        Assert.Equal(2, stored.Version);
    }
}
