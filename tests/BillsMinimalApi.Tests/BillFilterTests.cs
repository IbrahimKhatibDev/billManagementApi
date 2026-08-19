using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The filtering, searching and sorting half of <c>GET /restapi/BillDtos</c>.
/// <para>
/// All of this used to happen in the Blazor circuit over a full copy of the
/// table. Moving it into SQL means the semantics now have to survive EF's
/// translation as well as being right, which is what these assert: not just
/// "the filter works" but that it means the same thing it meant in C#.
/// </para>
/// </summary>
public class BillFilterTests : ApiTestBase
{
    public BillFilterTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    // Read once. The server takes its own reading of today, so a test that
    // straddled midnight could arrange against one date and assert against
    // another; taking one reading here at least keeps the arrangement coherent.
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    // -- Status -------------------------------------------------------------

    [Fact]
    public async Task Status_all_is_the_default_and_holds_everything()
    {
        await CreateStatusMixAsync();

        Assert.Equal(4, (await Fixture.GetPageAsync()).TotalCount);
        Assert.Equal(4, (await Fixture.GetPageAsync("status=all")).TotalCount);
    }

    [Fact]
    public async Task Status_paid_and_unpaid_partition_the_set()
    {
        await CreateStatusMixAsync();

        var paid = await Fixture.GetPageAsync("status=paid");
        var unpaid = await Fixture.GetPageAsync("status=unpaid");

        Assert.Equal(new[] { "Paid and late" }, paid.Items.Select(b => b.PayeeName));
        Assert.Equal(
            new[] { "Due later", "Due today", "Overdue" },
            unpaid.Items.Select(b => b.PayeeName).Order());
    }

    [Fact]
    public async Task Status_overdue_is_unpaid_and_past_due_not_merely_past_due()
    {
        await CreateStatusMixAsync();

        var overdue = await Fixture.GetPageAsync("status=overdue");

        // "Paid and late" is past its due date too. Including it would make the
        // red rows on the bills page and this filter disagree, and the filter
        // would be the one that was wrong.
        Assert.Equal(new[] { "Overdue" }, overdue.Items.Select(b => b.PayeeName));
    }

    [Fact]
    public async Task A_bill_due_today_is_not_overdue()
    {
        await CreateStatusMixAsync();

        var overdue = await Fixture.GetPageAsync("status=overdue");

        Assert.DoesNotContain(overdue.Items, b => b.PayeeName == "Due today");
    }

    [Theory]
    [InlineData("status=OVERDUE", 1)]
    [InlineData("status=Overdue", 1)]
    [InlineData("status=nonsense", 4)]
    [InlineData("status=", 4)]
    public async Task An_unrecognised_status_falls_back_to_all(string query, int expected)
    {
        await CreateStatusMixAsync();

        Assert.Equal(expected, (await Fixture.GetPageAsync(query)).TotalCount);
    }

    // -- Sorting ------------------------------------------------------------

    [Theory]
    [InlineData("sort=payee&dir=asc", "Alpha,Bravo,Charlie,Delta,Echo")]
    [InlineData("sort=payee&dir=desc", "Echo,Delta,Charlie,Bravo,Alpha")]
    [InlineData("sort=amount&dir=asc", "Alpha,Bravo,Delta,Echo,Charlie")]
    [InlineData("sort=amount&dir=desc", "Charlie,Echo,Delta,Bravo,Alpha")]
    [InlineData("sort=dueDate&dir=asc", "Charlie,Echo,Delta,Alpha,Bravo")]
    [InlineData("sort=dueDate&dir=desc", "Bravo,Alpha,Delta,Echo,Charlie")]
    [InlineData("sort=id&dir=asc", "Delta,Alpha,Charlie,Bravo,Echo")]
    [InlineData("sort=id&dir=desc", "Echo,Bravo,Charlie,Alpha,Delta")]
    public async Task Every_sort_column_orders_in_both_directions(string query, string expected)
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync(query);

        Assert.Equal(expected.Split(','), page.Items.Select(b => b.PayeeName));
    }

    [Fact]
    public async Task Sorting_on_paid_puts_unpaid_first_and_breaks_ties_by_id()
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync("sort=paid&dir=asc");

        // false before true, and within each block the creation order — the
        // tiebreak, without which the order inside a block is undefined.
        Assert.Equal(
            new[] { "Delta", "Charlie", "Echo", "Alpha", "Bravo" },
            page.Items.Select(b => b.PayeeName));
    }

    [Theory]
    [InlineData("sort=nonsense&dir=asc")]
    [InlineData("sort=&dir=asc")]
    public async Task An_unrecognised_sort_falls_back_to_id(string query)
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync(query);

        Assert.Equal(page.Items.Select(b => b.Id).Order(), page.Items.Select(b => b.Id));
    }

    [Theory]
    [InlineData("dir=DESC")]
    [InlineData("dir=descending")]
    public async Task Direction_is_read_loosely(string dir)
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync($"sort=payee&{dir}");

        Assert.Equal("Echo", page.Items[0].PayeeName);
    }

    [Theory]
    [InlineData("dir=ascending")]
    [InlineData("dir=up")]
    [InlineData("dir=")]
    public async Task Anything_not_recognisably_descending_sorts_ascending(string dir)
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync($"sort=payee&{dir}");

        Assert.Equal("Alpha", page.Items[0].PayeeName);
    }

    // -- Search -------------------------------------------------------------

    [Theory]
    [InlineData("charlie")]
    [InlineData("CHARLIE")]
    [InlineData("harl")]
    public async Task Search_matches_a_payee_substring_in_any_case(string term)
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync($"search={term}");

        Assert.Equal(new[] { "Charlie" }, page.Items.Select(b => b.PayeeName));
    }

    [Fact]
    public async Task Search_matches_an_id_including_a_partial_one()
    {
        // Ids restart at 1 for every test, so these are 1 through 12 and none of
        // the payee names contains a digit — anything that comes back matched on
        // the id and nothing else.
        for (var i = 1; i <= 12; i++)
        {
            await Fixture.CreateBillAsync($"Payee {(char)('A' + i - 1)}");
        }

        var exact = await Fixture.GetPageAsync("search=7");
        var partial = await Fixture.GetPageAsync("search=1");

        Assert.Equal(new long[] { 7 }, exact.Items.Select(b => b.Id));

        // A substring match, not an equality one: typing "1" while looking for
        // bill 11 should not hide it behind bill 1.
        Assert.Equal(new long[] { 1, 10, 11, 12 }, partial.Items.Select(b => b.Id).Order());
    }

    [Fact]
    public async Task Search_finds_nothing_when_nothing_matches()
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync("search=zzzz");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Theory]
    [InlineData("search=")]
    [InlineData("search=%20%20")]
    public async Task A_blank_search_is_no_search_at_all(string query)
    {
        await CreateSortFixtureAsync();

        Assert.Equal(5, (await Fixture.GetPageAsync(query)).TotalCount);
    }

    [Fact]
    public async Task Search_is_trimmed_before_it_is_matched()
    {
        await CreateSortFixtureAsync();

        var page = await Fixture.GetPageAsync("search=%20Charlie%20");

        Assert.Equal(new[] { "Charlie" }, page.Items.Select(b => b.PayeeName));
    }

    [Theory]
    // A percent sign is a LIKE wildcard. Unescaped, "%" matches every row and
    // "50%" matches anything starting with 50 — a search box that quietly
    // becomes a pattern language is worse than one that finds nothing.
    // Both payees carry a literal percent sign, so both are the right answer —
    // where an unescaped "%" would have returned all six.
    [InlineData("%", "Ten % Discount,50% Off Ltd")]
    [InlineData("50%", "50% Off Ltd")]
    // An underscore matches any single character, so an unescaped "a_b" finds
    // "axb Holdings" — which is in the fixture precisely to fail this if so.
    [InlineData("_", "Data_base Ltd")]
    [InlineData("a_b", "Data_base Ltd")]
    // The escape character itself has to survive being escaped.
    [InlineData(@"\", @"Back\slash Inc")]
    public async Task Wildcards_in_a_search_are_matched_literally(string term, string expected)
    {
        // Six payees, of which only the ones holding the character itself should
        // ever come back. Without escaping, "%" matches all six and "a_b"
        // matches "axb Holdings".
        await Fixture.CreateBillAsync("Ten % Discount");
        await Fixture.CreateBillAsync("50% Off Ltd");
        await Fixture.CreateBillAsync("Data_base Ltd");
        await Fixture.CreateBillAsync(@"Back\slash Inc");
        await Fixture.CreateBillAsync("Plain Payee");
        await Fixture.CreateBillAsync("axb Holdings");

        var page = await Fixture.GetPageAsync($"search={Uri.EscapeDataString(term)}");

        Assert.Equal(expected.Split(','), page.Items.Select(b => b.PayeeName));
    }

    // -- Due-date window ----------------------------------------------------

    [Fact]
    public async Task The_window_is_inclusive_at_both_ends()
    {
        await CreateWindowFixtureAsync();

        var page = await Fixture.GetPageAsync("from=2026-04-01&to=2026-04-30&sort=dueDate&dir=asc");

        Assert.Equal(
            new[] { "First of April", "Mid April", "Last of April" },
            page.Items.Select(b => b.PayeeName));
    }

    [Fact]
    public async Task A_bill_due_late_on_the_last_day_of_the_window_is_still_in_it()
    {
        await CreateWindowFixtureAsync();

        var page = await Fixture.GetPageAsync("from=2026-04-01&to=2026-04-30");

        // Stored at 23:30, not at midnight. An upper bound written as
        // `DueDate <= to` compares against midnight and drops this row — which
        // is the bug the exclusive next-midnight bound exists to avoid, and it
        // only shows up on data whose due dates carry a time of day.
        Assert.Contains(page.Items, b => b.PayeeName == "Last of April");
    }

    [Fact]
    public async Task Each_end_of_the_window_can_be_left_open()
    {
        await CreateWindowFixtureAsync();

        var openEnd = await Fixture.GetPageAsync("from=2026-04-01");
        var openStart = await Fixture.GetPageAsync("to=2026-04-30");

        Assert.Equal(4, openEnd.TotalCount);
        Assert.Equal(4, openStart.TotalCount);
    }

    [Fact]
    public async Task The_window_composes_with_the_other_filters()
    {
        await CreateWindowFixtureAsync();

        var page = await Fixture.GetPageAsync("from=2026-04-01&to=2026-04-30&search=April&status=unpaid");

        Assert.Equal(3, page.TotalCount);
    }

    // -- Fixtures -----------------------------------------------------------

    /// <summary>
    /// Four bills covering the corners of the status filter: unpaid and past
    /// due, unpaid and due exactly today, unpaid and due later, and — the one
    /// that catches a filter written as "past due" — paid but past due.
    /// </summary>
    private async Task CreateStatusMixAsync()
    {
        await Fixture.CreateBillAsync("Overdue", dueDate: Today.AddDays(-10));
        await Fixture.CreateBillAsync("Due today", dueDate: Today);
        await Fixture.CreateBillAsync("Due later", dueDate: Today.AddDays(10));
        await Fixture.CreateBillAsync("Paid and late", paid: true, dueDate: Today.AddDays(-20));
    }

    /// <summary>
    /// Five bills whose payee, amount, due date, paid flag and id each impose a
    /// different order, so a sort assertion cannot pass by accident on a
    /// column the endpoint ignored.
    /// <para>
    /// The names are all capitalised the same way on purpose: mixed case would
    /// make the expected order depend on the database's collation rather than on
    /// the endpoint.
    /// </para>
    /// </summary>
    private async Task<List<BillDto>> CreateSortFixtureAsync()
    {
        static DateTime utc(int year, int month, int day) =>
            new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        return new List<BillDto>
        {
            await Fixture.CreateBillAsync("Delta", 300m, false, utc(2026, 3, 10)),
            await Fixture.CreateBillAsync("Alpha", 100m, true, utc(2026, 5, 1)),
            await Fixture.CreateBillAsync("Charlie", 500m, false, utc(2026, 1, 20)),
            await Fixture.CreateBillAsync("Bravo", 200m, true, utc(2026, 7, 15)),
            await Fixture.CreateBillAsync("Echo", 400m, false, utc(2026, 2, 5)),
        };
    }

    private async Task CreateWindowFixtureAsync()
    {
        await Fixture.CreateBillAsync(
            "March", dueDate: new DateTime(2026, 3, 31, 23, 59, 0, DateTimeKind.Utc));
        await Fixture.CreateBillAsync(
            "First of April", dueDate: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await Fixture.CreateBillAsync(
            "Mid April", dueDate: new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc));
        await Fixture.CreateBillAsync(
            "Last of April", dueDate: new DateTime(2026, 4, 30, 23, 30, 0, DateTimeKind.Utc));
        await Fixture.CreateBillAsync(
            "May", dueDate: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
