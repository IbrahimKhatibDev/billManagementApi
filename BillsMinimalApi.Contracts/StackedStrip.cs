namespace BillsMinimalApi.Contracts;

/// <summary>One band of a stacked strip: what it is, and how wide.</summary>
public readonly record struct StripSegment(string Label, int Count, decimal Amount, double Percent);

/// <summary>
/// Collapses a set of labelled amounts into shares of their own total.
/// <para>
/// Every input row comes back, including the empty ones. The strip skips
/// zero-width bands because a band no pixels wide is not a band; the legend keeps
/// them, because "nothing is over 90 days late" is the best line on the page and
/// a legend that silently loses rows between loads cannot say it.
/// </para>
/// </summary>
public static class StackedStrip
{
    public static List<StripSegment> FromAging(IEnumerable<AgingBucket> buckets)
    {
        var rows = buckets as IReadOnlyList<AgingBucket> ?? buckets.ToList();
        var total = rows.Sum(b => b.Amount);

        return rows
            .Select(b => new StripSegment(
                b.Label,
                b.Count,
                b.Amount,
                // Divided in decimal and cast once, rather than cast twice and
                // divided in double: the shares of an odd total close on exactly
                // 100 this way, and the bar ends flush with its container.
                total == 0 ? 0 : (double)(b.Amount / total) * 100))
            .ToList();
    }
}
