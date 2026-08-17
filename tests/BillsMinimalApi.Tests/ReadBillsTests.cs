using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

public class ReadBillsTests : ApiTestBase
{
    public ReadBillsTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GetAll_returns_an_empty_array_when_there_are_no_bills()
    {
        var response = await Client.GetAsync(Routes.Bills);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<BillDto>>())!);
    }

    [Fact]
    public async Task GetAll_returns_every_bill()
    {
        await Fixture.CreateBillAsync("Acme Corp");
        await Fixture.CreateBillAsync("Globex");
        await Fixture.CreateBillAsync("Initech");

        var bills = await Client.GetFromJsonAsync<List<BillDto>>(Routes.Bills);

        Assert.Equal(3, bills!.Count);
        Assert.Equal(
            new[] { "Acme Corp", "Globex", "Initech" },
            bills.Select(b => b.PayeeName).Order());
    }

    [Fact]
    public async Task GetById_returns_the_matching_bill()
    {
        var created = await Fixture.CreateBillAsync("Globex", paymentDue: 249.99m, paid: true);

        var response = await Client.GetAsync($"{Routes.Bills}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = await response.Content.ReadFromJsonAsync<BillDto>();

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Globex", fetched.PayeeName);
        Assert.Equal(249.99m, fetched.PaymentDue);
        Assert.True(fetched.Paid);
        Assert.Equal(created.Version, fetched.Version);
    }

    [Fact]
    public async Task GetById_returns_404_for_an_unknown_id()
    {
        var response = await Client.GetAsync($"{Routes.Bills}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
