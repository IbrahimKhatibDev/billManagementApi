using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// Which section of the Bills page a bill lands in. Five predicates that have to
/// be mutually exclusive and cover everything: a bill in two groups is counted
/// twice in two sums, and a bill in none silently disappears from a page that
/// claims to be the whole book.
/// </summary>
public sealed class DueWindowsTests
{
    // Wednesday. Its week runs Mon 17th to Sun 23rd, and its month ends Mon 31st.
    private static readonly DateTime Wednesday = new(2026, 8, 19);

    private static DateTime On(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void A_paid_bill_is_paid_however_late_it_was()
    {
        // Paid is checked before anything about the date, so settling a bill
        // moves it out of Late rather than leaving it in two groups at once.
        Assert.Equal(
            DueWindow.Paid,
            DueWindows.Classify(paid: true, On(2025, 1, 1), Wednesday));
    }

    [Fact]
    public void Yesterday_and_unpaid_is_late()
    {
        Assert.Equal(
            DueWindow.Late,
            DueWindows.Classify(paid: false, On(2026, 8, 18), Wednesday));
    }

    [Fact]
    public void Today_is_not_late_yet()
    {
        // A bill due today has all day to be paid. Calling it late would put
        // eight bills in the Late group that the Overview's sentence did not
        // count, because the API's own OverdueCount uses the same rule.
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, Wednesday, Wednesday));
    }

    [Fact]
    public void The_week_runs_to_its_sunday_inclusive()
    {
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, On(2026, 8, 23), Wednesday));

        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 24), Wednesday));
    }

    [Fact]
    public void The_month_runs_to_its_last_day_inclusive()
    {
        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 31), Wednesday));

        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, On(2026, 9, 1), Wednesday));
    }

    [Fact]
    public void A_week_that_runs_past_the_end_of_the_month_still_wins()
    {
        // Monday the 31st: this week ends Sun 6 September, this month ends
        // today. "Due this week" is checked first, so a bill due on the 3rd is
        // this week's problem rather than being pushed out to Later.
        var monthEnd = On(2026, 8, 31);

        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, On(2026, 9, 3), monthEnd));

        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, On(2026, 9, 7), monthEnd));
    }

    [Fact]
    public void On_a_sunday_the_week_ends_today_rather_than_seven_days_out()
    {
        // The prototype's `today + (7 - getDay())` gives next Sunday when today
        // is a Sunday, which would drag a whole extra week into "Due this week".
        // Weeks here start on Monday, the same as the timeline's buckets, so a
        // Sunday is the end of its own week.
        var sunday = On(2026, 8, 30);

        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, sunday, sunday));

        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 31), sunday));
    }

    [Fact]
    public void A_bill_with_no_due_date_falls_to_the_end_rather_than_vanishing()
    {
        // The API always sends one, but the client's Bill model makes DueDate
        // nullable so the create form can fail validation rather than crash.
        // Later is where an unknown date is least disruptive — it is not late,
        // and it is not being claimed as due this week.
        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, null, Wednesday));
    }

    [Fact]
    public void A_time_of_day_does_not_move_a_bill_between_groups()
    {
        // Due dates arrive as midnight UTC, but a bill edited through the form
        // can carry a local time. Comparing dates rather than instants is what
        // stops "due today at 09:00" reading as late by lunchtime.
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, Wednesday.AddHours(9), Wednesday.AddHours(17)));
    }

    [Fact]
    public void Every_window_has_a_title_and_they_come_in_reading_order()
    {
        Assert.Equal(
            new[] { "Late", "Due this week", "Due this month", "Later", "Paid" },
            DueWindows.Order.Select(DueWindows.Title));
    }
}
