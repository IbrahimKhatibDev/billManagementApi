using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The geometry behind the weekly timeline. It is arithmetic, and it is the part
/// that goes wrong silently — a bar drawn past its baseline or a "now" marker in
/// the wrong week looks like a rendering quirk rather than a bug, so it gets
/// tested away from the renderer.
/// </summary>
public sealed class TimelineLayoutTests
{
    // A Monday, so a week that starts here is a week the bucketing agrees with.
    private static readonly DateTime Monday = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private static WeekTotals Week(DateTime start, decimal paid, decimal unpaid, int bills = 1) =>
        new() { WeekStart = start, Bills = bills, Paid = paid, Unpaid = unpaid };

    [Fact]
    public void Paid_sits_on_the_baseline_and_unpaid_stacks_on_top_of_it()
    {
        // Settled money is the foundation of the bar: it is not going to move.
        var layout = TimelineLayout.Build(new[] { Week(Monday, paid: 250m, unpaid: 750m) }, Monday);
        var bar = layout.Bars[0];

        Assert.Equal(TimelineLayout.Baseline, bar.PaidY + bar.PaidHeight, 6);
        Assert.Equal(bar.PaidY, bar.UnpaidY + bar.UnpaidHeight, 6);
    }

    [Fact]
    public void A_week_at_the_axis_maximum_reaches_the_top_of_the_plot()
    {
        // $1,000 rounds to an axis of exactly $1,000, so this stack is full
        // height — and full height must land on the ceiling, not through it.
        var layout = TimelineLayout.Build(new[] { Week(Monday, paid: 250m, unpaid: 750m) }, Monday);

        Assert.Equal(1_000m, layout.AxisMax);
        Assert.Equal(TimelineLayout.PlotTop, layout.Bars[0].UnpaidY, 6);
    }

    [Fact]
    public void Weeks_keep_their_order_and_share_a_width()
    {
        var layout = TimelineLayout.Build(
            new[]
            {
                Week(Monday, 100m, 0m),
                Week(Monday.AddDays(7), 0m, 400m),
                Week(Monday.AddDays(14), 50m, 50m),
            },
            Monday);

        Assert.Equal(3, layout.Bars.Count);
        Assert.Equal(Monday.AddDays(14), layout.Bars[2].WeekStart);
        Assert.True(layout.Bars[0].X < layout.Bars[1].X);
        Assert.True(layout.Bars[1].X < layout.Bars[2].X);
        Assert.Equal(layout.Bars[0].Width, layout.Bars[2].Width, 6);
    }

    [Fact]
    public void Today_is_marked_where_it_falls_inside_its_own_week()
    {
        // Wednesday: two days into a seven-day slot, so two sevenths across it.
        // Marking the week boundary instead would put "now" up to six days out.
        var wednesday = Monday.AddDays(2);
        var layout = TimelineLayout.Build(new[] { Week(Monday, 100m, 100m) }, wednesday);
        var bar = layout.Bars[0];

        Assert.NotNull(layout.NowX);
        Assert.Equal(bar.X + (bar.Width * 2 / 7), layout.NowX!.Value, 6);
    }

    [Fact]
    public void A_today_outside_the_plotted_weeks_is_not_marked_at_the_edge()
    {
        // Reachable from the Overview: an account whose bills are all historic.
        // Clamping the marker to the last bar would assert that today is that
        // week, which is worse than not drawing it.
        var layout = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m) },
            Monday.AddDays(70));

        Assert.Null(layout.NowX);
    }

    [Fact]
    public void An_empty_week_still_gets_its_slot()
    {
        // WeekBuckets gap-fills, so quiet weeks arrive as zeroes. Dropping them
        // here would make the axis lie about how much time it covers.
        var layout = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m), Week(Monday.AddDays(7), 0m, 0m, bills: 0) },
            Monday);

        Assert.Equal(2, layout.Bars.Count);
        Assert.Equal(0d, layout.Bars[1].PaidHeight);
        Assert.Equal(0d, layout.Bars[1].UnpaidHeight);
    }

    [Fact]
    public void No_weeks_at_all_draws_nothing_rather_than_dividing_by_zero()
    {
        var layout = TimelineLayout.Build(Array.Empty<WeekTotals>(), Monday);

        Assert.Empty(layout.Bars);
        Assert.Null(layout.NowX);
        Assert.Equal(0m, layout.AxisMax);
    }

    [Theory]
    [InlineData(1_750, 2_000)]
    [InlineData(1_000, 1_000)]
    [InlineData(30, 50)]
    [InlineData(6, 10)]
    [InlineData(0, 0)]
    public void The_axis_rounds_up_to_a_number_a_person_would_write(decimal max, decimal expected)
    {
        Assert.Equal(expected, TimelineLayout.NiceAxisMax(max));
    }
}
