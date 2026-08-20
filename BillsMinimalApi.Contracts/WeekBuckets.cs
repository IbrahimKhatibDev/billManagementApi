namespace BillsMinimalApi.Contracts;

/// <summary>
/// Folds one-row-per-due-date into one-row-per-week.
/// <para>
/// It lives in Contracts, not in the API, because Contracts is the only
/// assembly both the Blazor app and the unit-test project reference — and
/// because it is arithmetic with no database in it, which is exactly the kind of
/// thing the integration suite should not have to boot a container to check.
/// </para>
/// </summary>
public static class WeekBuckets
{
    /// <summary>One distinct due date and what falls on it.</summary>
    public readonly record struct DayTotals(
        DateTime Day, int Bills, decimal Paid, decimal Unpaid);

    /// <summary>
    /// Monday of the week <paramref name="day"/> falls in. The +6 %7 shift is
    /// there because <see cref="DayOfWeek"/> numbers Sunday as 0.
    /// </summary>
    public static DateTime StartOfWeek(DateTime day) =>
        day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static List<WeekTotals> FromDays(IEnumerable<DayTotals> days, int maxWeeks)
    {
        var byWeek = new Dictionary<DateTime, WeekTotals>();

        foreach (var day in days)
        {
            var start = StartOfWeek(day.Day);

            if (!byWeek.TryGetValue(start, out var week))
            {
                week = new WeekTotals { WeekStart = start };
                byWeek[start] = week;
            }

            week.Bills += day.Bills;
            week.Paid += day.Paid;
            week.Unpaid += day.Unpaid;
        }

        if (byWeek.Count == 0)
        {
            return new List<WeekTotals>();
        }

        var first = byWeek.Keys.Min();
        var last = byWeek.Keys.Max();
        var span = ((last - first).Days / 7) + 1;

        if (span > maxWeeks)
        {
            // Complete, but no longer continuous. Every week holding a bill is
            // still here; only the empty ones between them are gone.
            return byWeek.Values.OrderBy(w => w.WeekStart).ToList();
        }

        var filled = new List<WeekTotals>(span);

        for (var start = first; start <= last; start = start.AddDays(7))
        {
            filled.Add(byWeek.TryGetValue(start, out var week)
                ? week
                : new WeekTotals { WeekStart = start });
        }

        return filled;
    }
}
