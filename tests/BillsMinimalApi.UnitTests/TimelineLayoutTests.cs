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
        //
        // Across the slot, not across the bar. With one week the slot is the whole
        // plot, and the bar is the middle 70% of it — measuring from the bar's
        // left edge put this at 422.4 when the true two-sevenths is 346.29.
        var wednesday = Monday.AddDays(2);
        var layout = TimelineLayout.Build(new[] { Week(Monday, 100m, 100m) }, wednesday);
        var slot = TimelineLayout.PlotRight - TimelineLayout.PlotLeft;

        Assert.NotNull(layout.NowX);
        Assert.Equal(TimelineLayout.PlotLeft + (slot * 2 / 7), layout.NowX!.Value, 6);
    }

    [Fact]
    public void The_marker_spans_the_whole_week_not_just_its_bar()
    {
        // The two ends are what the bar-relative arithmetic could never reach:
        // Monday sat 15% of a slot late and Sunday stopped 10% short, so the
        // marker never entered the gap between two bars and never reached the end
        // of the week it marks. A single week makes the slot the whole plot, so
        // both bounds are the plot's own.
        var week = new[] { Week(Monday, 100m, 100m) };
        var slot = TimelineLayout.PlotRight - TimelineLayout.PlotLeft;

        var monday = TimelineLayout.Build(week, Monday);
        Assert.Equal(TimelineLayout.PlotLeft, monday.NowX!.Value, 6);

        var sunday = TimelineLayout.Build(week, Monday.AddDays(6));
        Assert.Equal(TimelineLayout.PlotLeft + (slot * 6 / 7), sunday.NowX!.Value, 6);
    }

    [Fact]
    public void The_shaded_week_sits_on_the_week_boundary_and_not_under_the_marker()
    {
        // The chart shades the current week behind the bars and draws the marker
        // inside it. They answer different questions — which week, and which day
        // of it — so the band is on the slot's edges however far into the week
        // today is. Centring a slot-wide band on NowX would put it up to half a
        // week out, and on Sunday it would be shading next week.
        var weeks = new[] { Week(Monday, 100m, 0m), Week(Monday.AddDays(7), 100m, 0m) };
        var slot = (TimelineLayout.PlotRight - TimelineLayout.PlotLeft) / 2;

        foreach (var day in new[] { 0, 3, 6 })
        {
            var layout = TimelineLayout.Build(weeks, Monday.AddDays(7 + day));

            Assert.Equal(slot, layout.SlotWidth, 6);
            Assert.Equal(TimelineLayout.PlotLeft + slot, layout.NowSlotX!.Value, 6);
        }
    }

    [Fact]
    public void A_month_label_gives_way_to_now_rather_than_printing_over_it()
    {
        // Both labels are on the axis now, and today falls in the week that opens
        // a month about one week in four. Twenty weeks is the sort of span the
        // Overview actually draws, and at that width a slot is narrower than the
        // two labels side by side — so "Sep" and "now" print as one smudge.
        // Only the month is droppable, so the month drops; the months either
        // side of it still say where in the year the reader is.
        var weeks = Enumerable.Range(0, 20)
            .Select(i => Week(Monday.AddDays(7 * i), 100m, 0m))
            .ToArray();

        // Sep 9: the Wednesday of the week starting Sep 7, which is the week
        // that opens September on this axis.
        var layout = TimelineLayout.Build(weeks, Monday.AddDays(23));

        Assert.NotNull(layout.NowX);
        Assert.DoesNotContain("Sep", layout.Ticks.Select(t => t.Label));
        Assert.Contains("Aug", layout.Ticks.Select(t => t.Label));
        Assert.Contains("Oct", layout.Ticks.Select(t => t.Label));
    }

    [Fact]
    public void A_month_label_with_room_to_spare_is_left_alone()
    {
        // The other side of the rule. Four weeks make the slots wide, so August's
        // label sits a long way from a marker in the same week — dropping it
        // would cost the axis a label there was room for.
        var layout = TimelineLayout.Build(
            new[]
            {
                Week(Monday, 100m, 0m),
                Week(Monday.AddDays(7), 100m, 0m),
                Week(Monday.AddDays(14), 100m, 0m),
                Week(Monday.AddDays(21), 100m, 0m),
            },
            Monday);

        Assert.Equal(new[] { "Aug", "Sep" }, layout.Ticks.Select(t => t.Label));
    }

    [Fact]
    public void A_crowded_axis_loses_one_month_to_the_marker_rather_than_a_run_of_them()
    {
        // The clearance is a fixed width; the slot is not. At MaxWeeks the slot
        // is about 4.5 units and the months roughly 20 apart, so an uncapped
        // clearance of 30 would clear the three labels either side of the marker
        // and leave a gap in the axis where the reader is looking. Whatever else
        // it drops, it must not drop the neighbours of the month it is standing
        // in — here, today is in August and July and September have to survive.
        // Centred on Monday, so today has 130 weeks of axis either side of it and
        // the neighbouring months are really there to be dropped.
        var weeks = Enumerable.Range(0, 260)
            .Select(i => Week(Monday.AddDays(7 * (i - 130)), 100m, 0m))
            .ToArray();

        var layout = TimelineLayout.Build(weeks, Monday);
        var labels = layout.Ticks.Select(t => t.Label).ToList();

        Assert.NotNull(layout.NowX);
        Assert.Contains("Jul 26", labels);
        Assert.Contains("Sep 26", labels);
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

        // And nothing to shade either: a band with no marker in it would still
        // be pointing at a week and calling it this one.
        Assert.Null(layout.NowSlotX);
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

    [Fact]
    public void One_label_per_month_rather_than_one_per_week()
    {
        // Aug 17, 24 and 31, then Sep 7: four bars, two labels.
        var layout = TimelineLayout.Build(
            new[]
            {
                Week(Monday, 100m, 0m),
                Week(Monday.AddDays(7), 100m, 0m),
                Week(Monday.AddDays(14), 100m, 0m),
                Week(Monday.AddDays(21), 100m, 0m),
            },
            Monday);

        Assert.Equal(4, layout.Bars.Count);
        Assert.Equal(new[] { "Aug", "Sep" }, layout.Ticks.Select(t => t.Label));
    }

    [Fact]
    public void The_same_month_a_year_apart_gets_its_own_label()
    {
        // WeekBuckets drops the empty weeks between bills once the span passes
        // MaxWeeks, so two neighbouring entries can be the same month in
        // different years. Keyed on the month number alone, the second one was
        // suppressed as a repeat and a year of the chart went unlabelled.
        var layout = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m), Week(Monday.AddYears(1), 100m, 0m) },
            Monday);

        Assert.Equal(2, layout.Ticks.Count);
    }

    [Fact]
    public void A_book_spanning_more_than_one_year_says_which_year()
    {
        // "Aug" twice on the same axis is two labels a reader cannot tell apart.
        // The year only appears when there is more than one to confuse.
        var oneYear = TimelineLayout.Build(new[] { Week(Monday, 100m, 0m) }, Monday);
        Assert.Equal("Aug", oneYear.Ticks[0].Label);

        var twoYears = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m), Week(Monday.AddYears(1), 100m, 0m) },
            Monday);
        Assert.Equal(new[] { "Aug 26", "Aug 27" }, twoYears.Ticks.Select(t => t.Label));
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
