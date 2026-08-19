using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Contracts;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

public class ReadBillsTests : ApiTestBase
{
    public ReadBillsTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GetAll_returns_an_empty_page_when_there_are_no_bills()
    {
        var response = await Client.GetAsync(Routes.Bills);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = (await response.Content.ReadFromJsonAsync<PagedResult<BillDto>>())!;

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);

        // Not zero: a client rendering "page 1 of 0" reads as a bug, and the
        // row numbers have to collapse rather than say "showing 1 to 0".
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(0, page.FirstRowNumber);
        Assert.False(page.HasNext);
        Assert.False(page.HasPrevious);
    }

    [Fact]
    public async Task GetAll_returns_every_bill_when_they_fit_on_one_page()
    {
        await Fixture.CreateBillAsync("Acme Corp");
        await Fixture.CreateBillAsync("Globex");
        await Fixture.CreateBillAsync("Initech");

        var page = await Fixture.GetPageAsync();

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            new[] { "Acme Corp", "Globex", "Initech" },
            page.Items.Select(b => b.PayeeName).Order());
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
