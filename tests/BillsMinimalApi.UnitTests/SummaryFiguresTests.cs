using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The figures on the Reports page that Postgres does not compute:
/// <see cref="BillSummary.OutstandingAmount"/>,
/// <see cref="BillSummary.PaidPercent"/> and their equivalents on the payee and
/// month rows.
/// <para>
/// The aggregates around them are covered end to end in the integration suite,
/// which is where they belong — they are SQL. These four are getters, so they
/// never appear in a response body for a test to assert against, and each one is
/// a division or a subtraction that the page then draws a bar from.
/// </para>
/// </summary>
public sealed class SummaryFiguresTests
{
    [Fact]
    public void Outstanding_is_what_is_billed_less_what_is_paid()
    {
        var summary = new BillSummary { TotalBilled = 1_250.75m, PaidAmount = 400.25m };

        Assert.Equal(850.50m, summary.OutstandingAmount);
    }

    [Fact]
    public void Money_subtracts_exactly_because_it_is_never_a_double()
    {
        // 0.3 - 0.1 is 0.19999999999999998 in binary floating point, and this
        // figure is rendered to two places next to two others that must add up to
        // it. decimal is the reason the page never shows a penny that came from
        // nowhere.
        var summary = new BillSummary { TotalBilled = 0.3m, PaidAmount = 0.1m };

        Assert.Equal(0.2m, summary.OutstandingAmount);
    }

    [Fact]
    public void Nothing_billed_is_nothing_paid_rather_than_a_division_by_zero()
    {
        // Reachable from the UI: any preset that no bill falls into gives an
        // empty summary, and the donut asks for this figure before it asks
        // whether there is anything to draw.
        var summary = new BillSummary { TotalBilled = 0m, PaidAmount = 0m };

        Assert.Equal(0d, summary.PaidPercent);
        Assert.Equal(0m, summary.OutstandingAmount);
    }

    [Fact]
    public void The_paid_share_is_of_money_and_not_of_bills()
    {
        // Ten £10 bills settled and one £500 bill outstanding. Counting bills
        // would call that 91% done; counting money calls it 17%, and 17% is the
        // number that describes what is still owed.
        var summary = new BillSummary
        {
            BillCount = 11,
            TotalBilled = 600m,
            PaidAmount = 100m,
        };

        Assert.Equal(16.67, summary.PaidPercent, 2);
        Assert.Equal(500m, summary.OutstandingAmount);
    }

    [Fact]
    public void Everything_settled_is_a_hundred_and_not_a_fraction_under()
    {
        // The donut closes on exactly 100 rather than 99.99999999999999, which
        // is what the same division in double would leave for a third of an
        // odd amount.
        var summary = new BillSummary { TotalBilled = 333.33m, PaidAmount = 333.33m };

        Assert.Equal(100d, summary.PaidPercent);
    }

    [Fact]
    public void A_payee_row_carries_its_own_outstanding_figure()
    {
        var payee = new PayeeTotals { Payee = "Acme", Bills = 4, Billed = 900m, Paid = 250m };

        Assert.Equal(650m, payee.Outstanding);
    }

    [Fact]
    public void A_month_row_does_the_same_arithmetic_over_its_own_month()
    {
        var month = new MonthTotals { Year = 2026, Month = 3, Bills = 5, Billed = 400m, Paid = 300m };

        Assert.Equal(100m, month.Outstanding);
        Assert.Equal(75d, month.PaidPercent);
    }

    [Fact]
    public void A_month_with_no_bills_in_it_draws_an_empty_bar_rather_than_throwing()
    {
        // The month table always shows a fixed run of months, so a quiet month is
        // a row with zeroes in it and not a row that is missing.
        var month = new MonthTotals { Year = 2026, Month = 3, Bills = 0, Billed = 0m, Paid = 0m };

        Assert.Equal(0d, month.PaidPercent);
        Assert.Equal(0m, month.Outstanding);
    }

    [Fact]
    public void A_month_row_can_be_placed_on_a_calendar_without_being_told_a_date()
    {
        // Year and month rather than a formatted label, because how "March 2026"
        // is spelled is the client's business. FirstDay is what lets the client
        // do the spelling.
        var month = new MonthTotals { Year = 2026, Month = 3 };

        Assert.Equal(new DateTime(2026, 3, 1), month.FirstDay);
    }
}
