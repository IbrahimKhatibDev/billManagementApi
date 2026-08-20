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
public sealed record TimelineLayout(
    List<TimelineBar> Bars,
    List<TimelineTick> Ticks,
    double? NowX,
    decimal AxisMax)
{
    public const double PlotLeft = 8;
    public const double PlotRight = 1192;
    public const double PlotTop = 12;
    public const double Baseline = 168;

    private const double PlotHeight = Baseline - PlotTop;

    /// <summary>Share of each week's slot the bar itself occupies; the rest is
    /// the gap to its neighbour.</summary>
    private const double BarFill = 0.7;

    public static TimelineLayout Build(IReadOnlyList<WeekTotals> weeks, DateTime today)
    {
        if (weeks.Count == 0)
        {
            return new TimelineLayout(new(), new(), null, 0m);
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

        return new TimelineLayout(bars, ticks, ComputeNowX(bars, slot, today), axisMax);
    }

    private static double Scale(decimal amount, decimal axisMax) =>
        axisMax == 0 ? 0 : (double)(amount / axisMax) * PlotHeight;

    /// <summary>
    /// Where "today" sits along the axis, or null when it is not on the plot at
    /// all — an account of nothing but historic bills has no now to mark, and a
    /// marker pinned to the last bar would claim otherwise.
    /// <para>
    /// Named <c>ComputeNowX</c> rather than <c>NowX</c>: the record's own
    /// <see cref="NowX"/> property already owns that identifier, and a method
    /// sharing it with a property is a compile error (CS0102), not an override.
    /// </para>
    /// </summary>
    private static double? ComputeNowX(List<TimelineBar> bars, double slot, DateTime today)
    {
        var thisWeek = WeekBuckets.StartOfWeek(today);
        var index = bars.FindIndex(b => b.WeekStart == thisWeek);

        if (index < 0)
        {
            return null;
        }

        // Proportional within the week, not on its boundary: a Friday marker on
        // Monday's line is up to four days of error on a chart about timing.
        var dayOfWeek = (today.Date - thisWeek).TotalDays;

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
