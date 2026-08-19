using System.Net;
using BillsMinimalApi.Contracts;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The paging half of <c>GET /restapi/BillDtos</c>.
/// <para>
/// The endpoint used to return the whole table and leave the client to slice it,
/// so these are the tests that did not exist before there was anything to get
/// wrong. The ones that matter most are the boundaries: what the last page
/// holds, what a page past the end does, and whether walking every page sees
/// every bill exactly once.
/// </para>
/// </summary>
public class BillPagingTests : ApiTestBase
{
    public BillPagingTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task A_page_is_bounded_by_page_size_while_the_count_describes_the_whole_set()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync("page=1&pageSize=10");

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);

        // The point of sending TotalCount at all: without it the client cannot
        // tell 10 of 25 from 10 of 10.
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNext);
        Assert.False(page.HasPrevious);
    }

    [Fact]
    public async Task The_last_page_holds_the_remainder()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync("page=3&pageSize=10");

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(3, page.Page);
        Assert.Equal(21, page.FirstRowNumber);
        Assert.Equal(25, page.LastRowNumber);
        Assert.False(page.HasNext);
        Assert.True(page.HasPrevious);
    }

    [Fact]
    public async Task A_page_past_the_end_clamps_to_the_last_page()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync("page=99&pageSize=10");

        // Deliberately not an empty page. A client standing on page 5 of a set
        // that has since shrunk should land somewhere it can read, and the Page
        // it gets back tells it where it actually is.
        Assert.Equal(3, page.Page);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(25, page.TotalCount);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-4")]
    public async Task A_nonsensical_page_falls_back_to_the_first(string query)
    {
        await CreateBillsAsync(3);

        var page = await Fixture.GetPageAsync(query);

        Assert.Equal(1, page.Page);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task A_non_numeric_page_is_rejected_rather_than_ignored()
    {
        // The one place this endpoint does not shrug. `status`, `sort` and `dir`
        // fall back to a default because they name things a user can mistype;
        // `page` is bound as an int, so the framework rejects a value that is
        // not one before BillQuery ever sees it. Asserted rather than assumed —
        // it is the difference between a broken pager and a broken page.
        var response = await Client.GetAsync($"{Routes.Bills}?page=notanumber");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Page_size_defaults_when_none_is_asked_for()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync();

        Assert.Equal(BillQuery.DefaultPageSize, page.PageSize);
        Assert.Equal(BillQuery.DefaultPageSize, page.Items.Count);
        Assert.Equal(25, page.TotalCount);
    }

    [Fact]
    public async Task Page_size_is_clamped_so_a_client_cannot_ask_for_the_table()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync("pageSize=100000");

        // The clamp, not the default, is what bounds the work — a default is
        // only a suggestion the caller can talk you out of.
        Assert.Equal(BillQuery.MaxPageSize, page.PageSize);
        Assert.Equal(25, page.Items.Count);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    public async Task A_non_positive_page_size_clamps_to_one(string query)
    {
        await CreateBillsAsync(3);

        var page = await Fixture.GetPageAsync(query);

        Assert.Equal(1, page.PageSize);
        Assert.Single(page.Items);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task Walking_every_page_sees_every_bill_exactly_once()
    {
        // Sorted on Paid, which has two distinct values across 25 rows — the
        // worst case for paging stability. Without the id tiebreak in ApplySort,
        // Postgres is free to order the ties differently for each OFFSET, and a
        // walk like this one shows the same bill twice while never showing
        // another. That bug is invisible on a column with distinct values, which
        // is exactly why it is worth a test.
        var created = await CreateBillsAsync(25);

        var seen = new List<long>();

        for (var page = 1; page <= 5; page++)
        {
            var result = await Fixture.GetPageAsync($"page={page}&pageSize=5&sort=paid&dir=desc");
            seen.AddRange(result.Items.Select(b => b.Id));
        }

        Assert.Equal(created.Count, seen.Count);
        Assert.Equal(created.Select(b => b.Id).Order(), seen.Order());
    }

    [Fact]
    public async Task Filters_are_applied_before_the_count_not_after_it()
    {
        await CreateBillsAsync(25);

        var page = await Fixture.GetPageAsync("status=paid&pageSize=5");

        // 25 bills, every third paid. A TotalCount of 25 here would mean the
        // count was taken over the table and the filter applied to the page —
        // the pager would then offer five pages of which three are empty.
        var expected = Enumerable.Range(1, 25).Count(i => i % 3 == 0);

        Assert.Equal(expected, page.TotalCount);
        Assert.All(page.Items, b => Assert.True(b.Paid));
    }

    /// <summary>
    /// Creates <paramref name="count"/> bills whose payee, amount, due date and
    /// paid flag all vary independently, so a sort on any one column has a
    /// different expected order from a sort on the others.
    /// </summary>
    private async Task<List<BillDto>> CreateBillsAsync(int count)
    {
        var bills = new List<BillDto>();

        for (var i = 1; i <= count; i++)
        {
            bills.Add(await Fixture.CreateBillAsync(
                payeeName: $"Payee {i:00}",
                paymentDue: i * 10m,
                paid: i % 3 == 0,
                dueDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i)));
        }

        return bills;
    }
}
