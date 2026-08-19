using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.Tests;

/// <summary>
/// <c>GET /restapi/BillDtos/summary</c>, which is the Reports page.
/// <para>
/// The page used to fetch every bill and add it up in the browser circuit. These
/// tests exist because moving that arithmetic into Postgres is only worth doing
/// if the answers do not change: a report that quietly started rounding
/// differently, or moved a bill from one aging bucket to the next, would look
/// exactly like a report that had always been wrong.
/// </para>
/// <para>
/// The bucket edges get their own tests for the same reason. Off-by-one at a
/// boundary is the failure mode nobody notices, because every figure still looks
/// plausible.
/// </para>
/// </summary>
public class BillSummaryTests : ApiTestBase
{
    public BillSummaryTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    // -- Nothing to report --------------------------------------------------

    [Fact]
    public async Task An_empty_range_reports_zeroes_rather_than_failing()
    {
        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(0, summary.BillCount);
        Assert.Equal(0m, summary.TotalBilled);
        Assert.Equal(0m, summary.PaidAmount);
        Assert.Equal(0m, summary.OutstandingAmount);
        Assert.Equal(0m, summary.AverageBill);
        Assert.Equal(0m, summary.MedianBill);

        // Not NaN. Nothing billed is 0% settled, and a division here would have
        // rendered as "NaN%" on the page.
        Assert.Equal(0, summary.PaidPercent);

        Assert.Null(summary.LargestBill);
        Assert.Empty(summary.Payees);
        Assert.Empty(summary.Months);
        Assert.Empty(summary.Priority);
    }

    [Fact]
    public async Task The_fixed_bands_are_always_present_even_when_empty()
    {
        var summary = await Fixture.GetSummaryAsync();

        // Five aging buckets and five size bands, always, in order. A table
        // whose rows appear and vanish as the range changes cannot be read
        // across presets, and an empty "over 90 days late" is information.
        Assert.Equal(
            new[] { "Not yet due", "1–30 days late", "31–60 days late", "61–90 days late", "Over 90 days late" },
            summary.Aging.Select(a => a.Label));

        Assert.Equal(
            new[] { "Under $50", "$50 – $99", "$100 – $249", "$250 – $499", "$500 and over" },
            summary.SizeBands.Select(b => b.Label));

        Assert.All(summary.Aging, a => Assert.Equal(0, a.Count));
        Assert.All(summary.SizeBands, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public async Task The_response_states_the_date_it_was_computed_against()
    {
        var summary = await Fixture.GetSummaryAsync();

        // The client renders "overdue" against the server's idea of today rather
        // than the browser's, which is the only way the shading and the counts
        // agree for a user in another timezone.
        Assert.Equal(Today, summary.AsOf);
    }

    // -- Headline figures ---------------------------------------------------

    [Fact]
    public async Task The_headline_figures_add_up()
    {
        await CreateHeadlineFixtureAsync();

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(4, summary.BillCount);
        Assert.Equal(750m, summary.TotalBilled);
        Assert.Equal(100m, summary.PaidAmount);
        Assert.Equal(650m, summary.OutstandingAmount);
        Assert.Equal(3, summary.UnpaidCount);
        Assert.Equal(187.50m, summary.AverageBill);
        Assert.Equal(150m, summary.MedianBill);
        Assert.Equal(100d / 750d * 100d, summary.PaidPercent, 10);
    }

    [Fact]
    public async Task Overdue_counts_unpaid_bills_only()
    {
        await CreateHeadlineFixtureAsync();

        var summary = await Fixture.GetSummaryAsync();

        // The paid bill is ten days past its due date. Counting it would make
        // the reports page and the bills page disagree about what red means.
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(200m, summary.OverdueAmount);
    }

    [Fact]
    public async Task Due_soon_is_the_next_thirty_days_inclusive_of_both_ends()
    {
        await Fixture.CreateBillAsync("Yesterday", 1m, dueDate: Today.AddDays(-1));
        await Fixture.CreateBillAsync("Today", 2m, dueDate: Today);
        await Fixture.CreateBillAsync("Day thirty", 4m, dueDate: Today.AddDays(30));
        await Fixture.CreateBillAsync("Day thirty one", 8m, dueDate: Today.AddDays(31));
        await Fixture.CreateBillAsync("Paid, due soon", 16m, paid: true, dueDate: Today.AddDays(5));

        var summary = await Fixture.GetSummaryAsync();

        // Today counts, day 30 counts, day 31 does not, and a bill already paid
        // is not something to pay soon.
        Assert.Equal(2, summary.DueSoonCount);
        Assert.Equal(6m, summary.DueSoonAmount);
    }

    [Theory]
    [InlineData(new[] { 10, 20, 30 }, 20)]
    [InlineData(new[] { 10, 20, 30, 40 }, 25)]
    [InlineData(new[] { 7 }, 7)]
    [InlineData(new[] { 10, 40 }, 25)]
    public async Task The_median_is_the_middle_or_the_mean_of_the_middle_two(
        int[] amounts, int expected)
    {
        // Inserted out of order on purpose: the median comes from an ORDER BY in
        // SQL, not from the order the rows happen to be stored in.
        foreach (var amount in amounts.OrderByDescending(a => a))
        {
            await Fixture.CreateBillAsync($"Payee {amount}", amount);
        }

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(expected, summary.MedianBill);
    }

    [Fact]
    public async Task The_largest_bill_is_named_with_the_days_it_is_late()
    {
        await CreateHeadlineFixtureAsync();

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal("Later, larger", summary.LargestBill!.PayeeName);
        Assert.Equal(400m, summary.LargestBill.PaymentDue);

        // Not yet due, so zero rather than a negative number of days late.
        Assert.Equal(0, summary.LargestBill.DaysLate);
    }

    // -- Aging --------------------------------------------------------------

    [Fact]
    public async Task Aging_buckets_split_on_the_days_they_are_named_after()
    {
        // One unpaid bill at each edge, all worth $10, so the counts and the
        // amounts have to agree with each other as well as with the buckets.
        int[] daysLate = { -5, 0, 1, 30, 31, 60, 61, 90, 91 };

        foreach (var days in daysLate)
        {
            await Fixture.CreateBillAsync($"Late {days}", 10m, dueDate: Today.AddDays(-days));
        }

        var summary = await Fixture.GetSummaryAsync();

        // Not yet due: due in five days and due today. Then 1 and 30 together,
        // 31 and 60 together, 61 and 90 together, and 91 alone — the bill due
        // exactly 30 days ago is 30 days late, not 31.
        Assert.Equal(new[] { 2, 2, 2, 2, 1 }, summary.Aging.Select(a => a.Count));
        Assert.Equal(new[] { 20m, 20m, 20m, 20m, 10m }, summary.Aging.Select(a => a.Amount));
    }

    [Fact]
    public async Task Aging_ignores_paid_bills()
    {
        await Fixture.CreateBillAsync("Unpaid", 10m, dueDate: Today.AddDays(-45));
        await Fixture.CreateBillAsync("Paid", 999m, paid: true, dueDate: Today.AddDays(-45));

        var summary = await Fixture.GetSummaryAsync();

        // Aging answers "what do I still owe, and for how long" — a settled bill
        // has no age.
        Assert.Equal(new[] { 0, 0, 1, 0, 0 }, summary.Aging.Select(a => a.Count));
        Assert.Equal(10m, summary.Aging[2].Amount);
    }

    // -- Size bands ---------------------------------------------------------

    [Fact]
    public async Task Size_bands_split_on_the_amounts_they_are_named_after()
    {
        decimal[] amounts = { 49.99m, 50m, 99.99m, 100m, 249.99m, 250m, 499.99m, 500m, 1000m };

        foreach (var amount in amounts)
        {
            await Fixture.CreateBillAsync($"Payee {amount}", amount);
        }

        var summary = await Fixture.GetSummaryAsync();

        // Each band's lower bound is inclusive and its upper bound is not, so
        // exactly $50 is in the "$50 – $99" band and $49.99 is below it.
        Assert.Equal(new[] { 1, 2, 2, 2, 2 }, summary.SizeBands.Select(b => b.Count));
        Assert.Equal(1500m, summary.SizeBands[4].Total);
    }

    [Fact]
    public async Task Size_bands_describe_every_bill_not_only_the_unpaid_ones()
    {
        await Fixture.CreateBillAsync("Paid", 300m, paid: true);
        await Fixture.CreateBillAsync("Unpaid", 300m);

        var summary = await Fixture.GetSummaryAsync();

        // This section is about what the bills look like, not about what is
        // owed — dropping the paid ones would make it a second aging table.
        Assert.Equal(2, summary.SizeBands[3].Count);
    }

    // -- Payees -------------------------------------------------------------

    [Fact]
    public async Task Payees_are_grouped_ignoring_case_and_surrounding_space()
    {
        await Fixture.CreateBillAsync("Acme Corp", 100m);
        await Fixture.CreateBillAsync("ACME CORP", 200m, paid: true);
        await Fixture.CreateBillAsync("  acme corp  ", 300m);
        await Fixture.CreateBillAsync("Globex", 50m);

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(2, summary.Payees.Count);

        var acme = summary.Payees.Single(p => p.Bills == 3);

        // Which of the three spellings is displayed is the database collation's
        // business, so this asserts what the grouping was for rather than which
        // spelling won.
        Assert.Equal("acme corp", acme.Payee.Trim(), ignoreCase: true);
        Assert.Equal(600m, acme.Billed);
        Assert.Equal(200m, acme.Paid);
        Assert.Equal(400m, acme.Outstanding);
    }

    [Fact]
    public async Task Payees_lead_with_the_biggest_debt()
    {
        await Fixture.CreateBillAsync("Small debt", 10m);
        await Fixture.CreateBillAsync("Big debt", 500m);
        await Fixture.CreateBillAsync("Settled", 900m, paid: true);

        var summary = await Fixture.GetSummaryAsync();

        // Sorted by what is outstanding, not by what was billed: the $900 payee
        // owes nothing and belongs last.
        Assert.Equal(
            new[] { "Big debt", "Small debt", "Settled" },
            summary.Payees.Select(p => p.Payee));
    }

    // -- Months -------------------------------------------------------------

    [Fact]
    public async Task Months_are_newest_first_and_carry_their_own_totals()
    {
        await Fixture.CreateBillAsync("Jan a", 100m, dueDate: Utc(2026, 1, 5));
        await Fixture.CreateBillAsync("Jan b", 200m, paid: true, dueDate: Utc(2026, 1, 25));
        await Fixture.CreateBillAsync("Mar", 300m, dueDate: Utc(2026, 3, 15));
        await Fixture.CreateBillAsync("Next Jan", 400m, dueDate: Utc(2027, 1, 5));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(
            new[] { (2027, 1), (2026, 3), (2026, 1) },
            summary.Months.Select(m => (m.Year, m.Month)));

        var january = summary.Months.Single(m => m is { Year: 2026, Month: 1 });

        Assert.Equal(2, january.Bills);
        Assert.Equal(300m, january.Billed);
        Assert.Equal(200m, january.Paid);
        Assert.Equal(100m, january.Outstanding);
        Assert.Equal(new DateTime(2026, 1, 1), january.FirstDay);
    }

    // -- Priority -----------------------------------------------------------

    [Fact]
    public async Task Priority_lists_the_latest_first_then_whatever_is_due_soonest()
    {
        await Fixture.CreateBillAsync("Due in 3", dueDate: Today.AddDays(3));
        await Fixture.CreateBillAsync("Late by 40", dueDate: Today.AddDays(-40));
        await Fixture.CreateBillAsync("Due in 1", dueDate: Today.AddDays(1));
        await Fixture.CreateBillAsync("Late by 2", dueDate: Today.AddDays(-2));
        await Fixture.CreateBillAsync("Settled", paid: true, dueDate: Today.AddDays(-90));

        var summary = await Fixture.GetSummaryAsync();

        // Everything already late, oldest first; then everything not yet due,
        // soonest first. And nothing that is already paid.
        Assert.Equal(
            new[] { "Late by 40", "Late by 2", "Due in 1", "Due in 3" },
            summary.Priority.Select(b => b.PayeeName));

        Assert.Equal(new[] { 40, 2, 0, 0 }, summary.Priority.Select(b => b.DaysLate));
    }

    [Fact]
    public async Task Priority_is_a_shortlist_not_a_second_bills_page()
    {
        for (var i = 1; i <= 20; i++)
        {
            await Fixture.CreateBillAsync($"Late by {i}", dueDate: Today.AddDays(-i));
        }

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(BillSummary.PriorityCount, summary.Priority.Count);
        Assert.Equal("Late by 20", summary.Priority[0].PayeeName);
    }

    // -- Window -------------------------------------------------------------

    [Fact]
    public async Task The_window_scopes_every_section_of_the_report()
    {
        await Fixture.CreateBillAsync("Before", 1000m, dueDate: Utc(2026, 3, 31));
        await Fixture.CreateBillAsync("Inside", 100m, dueDate: Utc(2026, 4, 15));
        await Fixture.CreateBillAsync("Also inside", 200m, dueDate: Utc(2026, 4, 30));
        await Fixture.CreateBillAsync("After", 1000m, dueDate: Utc(2026, 5, 1));

        var summary = await Fixture.GetSummaryAsync("from=2026-04-01&to=2026-04-30");

        Assert.Equal(new DateTime(2026, 4, 1), summary.From);
        Assert.Equal(new DateTime(2026, 4, 30), summary.To);

        // The headline, the bands, the payee table and the month table all come
        // from one filtered query — the reason this is a single endpoint rather
        // than several is that they cannot disagree about the window.
        Assert.Equal(2, summary.BillCount);
        Assert.Equal(300m, summary.TotalBilled);
        Assert.Equal(2, summary.Payees.Count);
        Assert.Equal(new[] { (2026, 4) }, summary.Months.Select(m => (m.Year, m.Month)));
        Assert.Equal(200m, summary.LargestBill!.PaymentDue);
        Assert.Equal(2, summary.Aging.Sum(a => a.Count));
        Assert.Equal(2, summary.SizeBands.Sum(b => b.Count));
        Assert.Equal(2, summary.Priority.Count);
    }

    [Fact]
    public async Task A_window_that_holds_nothing_reports_nothing()
    {
        await Fixture.CreateBillAsync("Outside", 100m, dueDate: Utc(2026, 4, 15));

        var summary = await Fixture.GetSummaryAsync("from=2026-01-01&to=2026-01-31");

        Assert.Equal(0, summary.BillCount);
        Assert.Null(summary.LargestBill);
        Assert.Equal(5, summary.Aging.Count);
    }

    // -- Fixtures -----------------------------------------------------------

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Four bills chosen so that every headline figure has a different answer:
    /// one paid but past due, one overdue, one due inside the next thirty days,
    /// and one due well beyond them.
    /// </summary>
    private async Task CreateHeadlineFixtureAsync()
    {
        await Fixture.CreateBillAsync("Paid, late", 100m, paid: true, dueDate: Today.AddDays(-10));
        await Fixture.CreateBillAsync("Overdue", 200m, dueDate: Today.AddDays(-5));
        await Fixture.CreateBillAsync("Due soon", 50m, dueDate: Today.AddDays(10));
        await Fixture.CreateBillAsync("Later, larger", 400m, dueDate: Today.AddDays(60));
    }
}
