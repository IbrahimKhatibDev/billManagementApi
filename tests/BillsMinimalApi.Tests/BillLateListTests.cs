namespace BillsMinimalApi.Tests;

/// <summary>
/// <c>BillSummary.Late</c> and <c>OldestDaysLate</c> — the list the Overview
/// triages from and the number its headline sentence quotes.
/// <para>
/// Dates are relative to the real clock rather than fixed, because "late" is a
/// comparison the server makes against its own today and a hard-coded date would
/// mean something different every time the suite ran.
/// </para>
/// </summary>
public sealed class BillLateListTests(PostgresApiFixture fixture) : ApiTestBase(fixture)
{
    private static DateTime DaysAgo(int days) => DateTime.UtcNow.Date.AddDays(-days);

    [Fact]
    public async Task The_late_list_is_oldest_first_because_that_is_the_order_you_pay_in()
    {
        await Fixture.CreateBillAsync(payeeName: "Newer", dueDate: DaysAgo(3));
        await Fixture.CreateBillAsync(payeeName: "Older", dueDate: DaysAgo(40));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(new[] { "Older", "Newer" }, summary.Late.Select(b => b.PayeeName));
        Assert.Equal(40, summary.Late[0].DaysLate);
    }

    [Fact]
    public async Task A_bill_that_is_paid_is_not_late_however_old_it_is()
    {
        await Fixture.CreateBillAsync(payeeName: "Settled", paid: true, dueDate: DaysAgo(90));
        await Fixture.CreateBillAsync(payeeName: "Outstanding", dueDate: DaysAgo(5));

        var summary = await Fixture.GetSummaryAsync();

        var late = Assert.Single(summary.Late);
        Assert.Equal("Outstanding", late.PayeeName);
    }

    [Fact]
    public async Task A_bill_not_yet_due_is_not_late_either()
    {
        await Fixture.CreateBillAsync(payeeName: "Upcoming", dueDate: DateTime.UtcNow.Date.AddDays(7));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Empty(summary.Late);
    }

    [Fact]
    public async Task The_oldest_figure_is_the_top_of_the_list_and_not_a_second_query()
    {
        await Fixture.CreateBillAsync(dueDate: DaysAgo(12));
        await Fixture.CreateBillAsync(dueDate: DaysAgo(156));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(156, summary.OldestDaysLate);
    }

    [Fact]
    public async Task Nothing_late_is_zero_days_rather_than_an_empty_headline()
    {
        // The Overview quotes this figure in a sentence, so it has to be a number
        // even on the happy day when nothing is late.
        await Fixture.CreateBillAsync(paid: true, dueDate: DaysAgo(30));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Empty(summary.Late);
        Assert.Equal(0, summary.OldestDaysLate);
    }

    [Fact]
    public async Task The_list_and_the_overdue_headline_describe_the_same_bills()
    {
        // The Overview shows both at once; they came from two queries and must
        // still agree.
        await Fixture.CreateBillAsync(paymentDue: 100m, dueDate: DaysAgo(10));
        await Fixture.CreateBillAsync(paymentDue: 250m, dueDate: DaysAgo(20));
        await Fixture.CreateBillAsync(paymentDue: 999m, paid: true, dueDate: DaysAgo(20));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(summary.OverdueCount, summary.Late.Count);
        Assert.Equal(summary.OverdueAmount, summary.Late.Sum(b => b.PaymentDue));
    }
}
