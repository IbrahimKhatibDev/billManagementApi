using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>One week's stacked bar, in SVG user units.</summary>
public readonly record struct TimelineBar(
    DateTime WeekStart,
    double X,
    double Width,
    double PaidY,
    double PaidHeight,
    double UnpaidY,
    double UnpaidHeight,
    decimal Paid,
    decimal Unpaid);

/// <summary>A month label on the axis, at the week that starts it.</summary>
public readonly record struct TimelineTick(double X, string Label);

/// <summary>
/// The whole weekly timeline worked out in advance, so the component is markup
/// and nothing else.
/// <para>
/// The plot is authored in fixed user units and scaled by the browser, matching
/// how the app's other charts are drawn: the numbers here are resolution
/// independent, and nothing has to measure the viewport — which would need JS
/// interop, which does not exist during a prerendered first render.
/// </para>
/// </summary>
/// <param name="NowSlotX">
/// The left edge of the week <see cref="NowX"/> falls in, or null on the same
/// terms. The chart shades this week behind the bars, and the shading has to be
/// on the week's boundaries — <see cref="NowX"/> is a point inside the week,
/// proportional to the day, and a band centred on it would sit up to half a
/// week out of true.
/// </param>
/// <param name="SlotWidth">
/// One week's share of the axis: the bar plus the gap to its neighbour. The bars
/// are a fraction of it; the shaded week is the whole of it.
/// </param>
public sealed record TimelineLayout(
    List<TimelineBar> Bars,
    List<TimelineTick> Ticks,
    double? NowX,
    decimal AxisMax,
    double? NowSlotX,
    double SlotWidth)
{
    /// <summary>
    /// The width of the coordinate space every X below is measured in. Public
    /// because the axis labels are HTML rather than SVG — they are placed at
    /// <c>X / ViewWidth</c> of the chart's width, which needs the divisor.
    /// </summary>
    public const double ViewWidth = 1200;

    /// <summary>
    /// The height of the same space, ending just below <see cref="Baseline"/>:
    /// the plot stops at the axis line, and the labels under it are laid out by
    /// the browser in their own row.
    /// </summary>
    public const double ViewHeight = 176;

    public const double PlotLeft = 8;
    public const double PlotRight = 1192;
    public const double PlotTop = 12;
    public const double Baseline = 168;

    private const double PlotHeight = Baseline - PlotTop;

    /// <summary>Share of each week's slot the bar itself occupies; the rest is
    /// the gap to its neighbour.</summary>
    private const double BarFill = 0.7;

    /// <summary>
    /// How much room a label on the axis needs to itself, in user units: half of
    /// "Aug 26" plus half of "now" at the 11px the axis is set in, which is the
    /// point where the two would start to touch.
    /// </summary>
    private const double LabelClearance = 30;

    public static TimelineLayout Build(IReadOnlyList<WeekTotals> weeks, DateTime today)
    {
        if (weeks.Count == 0)
        {
            return new TimelineLayout(new(), new(), null, 0m, null, 0);
        }

        var axisMax = NiceAxisMax(weeks.Max(w => w.Total));

        var slot = (PlotRight - PlotLeft) / weeks.Count;
        var width = slot * BarFill;

        // MaxWeeks is 260, so a book can legitimately span five years and draw
        // "Jan" five times with nothing to tell them apart. PaidRateStrip solves
        // this for its own axis the same way; it was not carried across when this
        // chart was written alongside it.
        var spansYears = weeks.Min(w => w.WeekStart.Year) != weeks.Max(w => w.WeekStart.Year);

        var bars = new List<TimelineBar>(weeks.Count);
        var ticks = new List<TimelineTick>();

        // Year and month, not the month alone: WeekBuckets drops the empty weeks
        // between bills once the span passes MaxWeeks, so two consecutive entries
        // can be the same month a year apart — and a tick keyed on the month
        // number would suppress the second one entirely.
        var lastMonth = (Year: 0, Month: 0);

        for (var i = 0; i < weeks.Count; i++)
        {
            var week = weeks[i];
            var x = PlotLeft + (slot * i) + ((slot - width) / 2);

            var paidHeight = Scale(week.Paid, axisMax);
            var unpaidHeight = Scale(week.Unpaid, axisMax);
            var paidY = Baseline - paidHeight;

            bars.Add(new TimelineBar(
                WeekStart: week.WeekStart,
                X: x,
                Width: width,
                PaidY: paidY,
                PaidHeight: paidHeight,
                // Unpaid rides on top of paid: what is settled is the part of
                // the bar that will not move again.
                UnpaidY: paidY - unpaidHeight,
                UnpaidHeight: unpaidHeight,
                Paid: week.Paid,
                Unpaid: week.Unpaid));

            // One label per month rather than per week — 260 week labels is a
            // grey smear, and the reader is orienting by month anyway.
            if ((week.WeekStart.Year, week.WeekStart.Month) != lastMonth)
            {
                lastMonth = (week.WeekStart.Year, week.WeekStart.Month);
                ticks.Add(new TimelineTick(
                    x + (width / 2),
                    week.WeekStart.ToString(
                        spansYears ? "MMM yy" : "MMM", CultureInfo.CurrentCulture)));
            }
        }

        var nowWeek = FindNowWeek(bars, today);
        var nowX = ComputeNowX(nowWeek, slot, today);

        // "now" and the month labels share one row along the axis, and today is
        // in the week that opens a month about one week in four — so the two sit
        // in the same slot and print over each other. The month gives way: its
        // neighbours still say where in the year the reader is, and "now" is the
        // one label that cannot be inferred from the ones either side of it.
        if (nowX is { } marker)
        {
            // Capped at half a slot, because the clearance is a fixed width and
            // the axis is not. MaxWeeks is 260, which puts a slot at 4.5 units
            // and the months 20 apart — a flat 30 would take out the three
            // labels either side of the marker and leave a hole in the axis
            // exactly where the reader is looking. Half a slot keeps this to
            // what it is for: the label in the marker's own week.
            var clearance = Math.Min(LabelClearance, slot / 2);
            ticks.RemoveAll(t => Math.Abs(t.X - marker) < clearance);
        }

        return new TimelineLayout(
            bars,
            ticks,
            nowX,
            axisMax,
            nowWeek < 0 ? null : PlotLeft + (slot * nowWeek),
            slot);
    }

    private static double Scale(decimal amount, decimal axisMax) =>
        axisMax == 0 ? 0 : (double)(amount / axisMax) * PlotHeight;

    /// <summary>
    /// Which bar holds today, or -1 when today is not on the plot at all — an
    /// account of nothing but historic bills has no now to mark, and a marker
    /// pinned to the last bar would claim otherwise.
    /// </summary>
    private static int FindNowWeek(List<TimelineBar> bars, DateTime today) =>
        bars.FindIndex(b => b.WeekStart == WeekBuckets.StartOfWeek(today));

    /// <summary>
    /// Where "today" sits along the axis, or null when
    /// <see cref="FindNowWeek"/> found no week to sit in.
    /// <para>
    /// Named <c>ComputeNowX</c> rather than <c>NowX</c>: the record's own
    /// <see cref="NowX"/> property already owns that identifier, and a method
    /// sharing it with a property is a compile error (CS0102), not an override.
    /// </para>
    /// </summary>
    private static double? ComputeNowX(int index, double slot, DateTime today)
    {
        if (index < 0)
        {
            return null;
        }

        // Proportional within the week, not on its boundary: a Friday marker on
        // Monday's line is up to four days of error on a chart about timing.
        var dayOfWeek = (today.Date - WeekBuckets.StartOfWeek(today)).TotalDays;

        // Across the week's whole slot, measured from where the slot starts —
        // not across the bar. The bar is BarFill (0.7) of the slot and inset by
        // half the remainder, so interpolating over it put Monday 0.15 of a slot
        // late and Sunday 0.1 of a slot early: the marker could never enter the
        // gap between bars, and so never reached the end of the week it marks.
        // That is most of the error this method exists to remove, reintroduced.
        return PlotLeft + (slot * index) + (slot * dayOfWeek / 7);
    }

    /// <summary>
    /// Rounds up to 1, 2 or 5 times a power of ten — the ladder chart libraries
    /// use, so the axis reads as money at any scale.
    /// </summary>
    public static decimal NiceAxisMax(decimal max)
    {
        if (max <= 0)
        {
            return 0;
        }

        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        var normalised = max / magnitude;

        var rounded = normalised switch
        {
            <= 1m => 1m,
            <= 2m => 2m,
            <= 5m => 5m,
            _ => 10m,
        };

        return rounded * magnitude;
    }
}
