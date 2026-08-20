using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The fold behind the cash-flow timeline. Postgres hands back one row per
/// distinct due date; this turns those into the columns the chart draws.
/// </summary>
public sealed class WeekBucketTests
{
    private const int MaxWeeks = BillSummary.MaxWeeks;

    private static DateTime Day(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_week_starts_on_Monday()
    {
        // 2026-08-19 is a Wednesday; its week began on the 17th.
        Assert.Equal(Day(2026, 8, 17), WeekBuckets.StartOfWeek(Day(2026, 8, 19)));
    }

    [Fact]
    public void Sunday_closes_the_week_rather_than_opening_the_next_one()
    {
        // The .NET DayOfWeek enum starts on Sunday, so this is the case the
        // shift in StartOfWeek exists for.
        Assert.Equal(Day(2026, 8, 17), WeekBuckets.StartOfWeek(Day(2026, 8, 23)));
    }

    [Fact]
    public void Days_inside_one_week_add_up_to_one_column()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 100m, 0m),
            new WeekBuckets.DayTotals(Day(2026, 8, 21), 2, 0m, 250m),
        }, MaxWeeks);

        var week = Assert.Single(weeks);
        Assert.Equal(Day(2026, 8, 17), week.WeekStart);
        Assert.Equal(3, week.Bills);
        Assert.Equal(100m, week.Paid);
        Assert.Equal(250m, week.Unpaid);
        Assert.Equal(350m, week.Total);
    }

    [Fact]
    public void Paid_and_unpaid_stay_apart_because_the_bar_is_stacked()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 18), 2, 40m, 60m),
        }, MaxWeeks);

        Assert.Equal(40m, weeks[0].Paid);
        Assert.Equal(60m, weeks[0].Unpaid);
    }

    [Fact]
    public void A_quiet_week_between_two_busy_ones_still_occupies_space()
    {
        // An empty column is the information — it says nothing falls due then.
        // Dropping it would slide the following week left and make the gap
        // invisible.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
            new WeekBuckets.DayTotals(Day(2026, 8, 31), 1, 0m, 200m),
        }, MaxWeeks);

        Assert.Equal(3, weeks.Count);
        Assert.Equal(Day(2026, 8, 24), weeks[1].WeekStart);
        Assert.Equal(0, weeks[1].Bills);
        Assert.Equal(0m, weeks[1].Total);
    }

    [Fact]
    public void Weeks_come_back_oldest_first_whatever_order_the_days_arrived_in()
    {
        // GroupBy in Postgres promises no ordering, so the fold has to impose it.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 31), 1, 0m, 200m),
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
        }, MaxWeeks);

        Assert.Equal(Day(2026, 8, 17), weeks[0].WeekStart);
        Assert.Equal(Day(2026, 8, 31), weeks[2].WeekStart);
    }

    [Fact]
    public void A_span_no_one_would_draw_keeps_the_bills_and_drops_the_gaps()
    {
        // One bill typed with the wrong year would otherwise turn the timeline
        // into ten thousand empty columns. The run stops being continuous rather
        // than stopping being complete.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
            new WeekBuckets.DayTotals(Day(2126, 8, 17), 1, 0m, 200m),
        }, MaxWeeks);

        Assert.Equal(2, weeks.Count);
        Assert.Equal(Day(2026, 8, 17), weeks[0].WeekStart);

        // 2126-08-17 is a Saturday, not a Monday — the brief's literal assertion
        // here (Day(2126, 8, 17)) doesn't match what its own StartOfWeek rule
        // produces for that date. Every other test in this file derives its
        // expected WeekStart from the Monday of the input day, so this one does
        // the same rather than special-casing this one date to skip bucketing.
        Assert.Equal(Day(2126, 8, 12), weeks[1].WeekStart);
    }

    [Fact]
    public void No_bills_is_no_weeks_rather_than_one_empty_one()
    {
        Assert.Empty(WeekBuckets.FromDays(Array.Empty<WeekBuckets.DayTotals>(), MaxWeeks));
    }

    [Fact]
    public void The_week_start_is_UTC_because_it_is_compared_against_database_dates()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 19), 1, 0m, 100m),
        }, MaxWeeks);

        Assert.Equal(DateTimeKind.Utc, weeks[0].WeekStart.Kind);
    }
}
