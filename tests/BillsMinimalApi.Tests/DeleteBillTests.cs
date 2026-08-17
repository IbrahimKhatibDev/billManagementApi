using System.Net;

namespace BillsMinimalApi.Tests;

public class DeleteBillTests : ApiTestBase
{
    public DeleteBillTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Delete_returns_204_and_the_bill_is_then_gone()
    {
        var created = await Fixture.CreateBillAsync();

        var deleted = await Client.DeleteAsync($"{Routes.Bills}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var fetched = await Client.GetAsync($"{Routes.Bills}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);

        var deletedAgain = await Client.DeleteAsync($"{Routes.Bills}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        var response = await Client.DeleteAsync($"{Routes.Bills}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
