using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

/// <summary>
/// Minimal APIs do not run DataAnnotations on their own, so the
/// <c>[Required]</c> and <c>[Range]</c> attributes on <see cref="BillDto"/> were
/// decorative until <c>AddValidation()</c> was switched on: an empty payee and a
/// negative amount were both accepted and persisted.
/// </summary>
public class ValidationTests : ApiTestBase
{
    public ValidationTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_with_an_empty_payee_returns_400(string payeeName)
    {
        var response = await Client.PostAsJsonAsync(Routes.Bills, new BillDto
        {
            PayeeName = payeeName,
            DueDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentDue = 42.50m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "PayeeName",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        await AssertNothingWasPersistedAsync();
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    public async Task Post_with_a_non_positive_amount_returns_400(decimal paymentDue)
    {
        var response = await Client.PostAsJsonAsync(Routes.Bills, new BillDto
        {
            PayeeName = "Acme Corp",
            DueDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentDue = paymentDue,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "PaymentDue",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        await AssertNothingWasPersistedAsync();
    }

    [Fact]
    public async Task Put_with_an_invalid_payload_returns_400_and_leaves_the_bill_alone()
    {
        var created = await Fixture.CreateBillAsync("Acme Corp", paymentDue: 100m);

        created.PayeeName = string.Empty;

        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/{created.Id}", created);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stored = await Client.GetFromJsonAsync<BillDto>($"{Routes.Bills}/{created.Id}");

        Assert.Equal("Acme Corp", stored!.PayeeName);
        Assert.Equal(1, stored.Version);
    }

    private async Task AssertNothingWasPersistedAsync()
    {
        var bills = await Client.GetFromJsonAsync<List<BillDto>>(Routes.Bills);
        Assert.Empty(bills!);
    }
}
