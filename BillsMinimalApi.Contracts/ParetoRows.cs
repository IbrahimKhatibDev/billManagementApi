using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// One payee's place in the ranking: what they are owed, what share of the
/// total that is, and what share the ranking has accounted for by this row.
/// </summary>
/// <param name="SharePercent">This payee alone, 0–100.</param>
/// <param name="CumulativePercent">
/// This payee and everyone above them, 0–100. The last row is 100 by
/// construction, since the denominator is the sum of the rows themselves.
/// </param>
public sealed record ParetoRow(
    string Payee,
    int Bills,
    decimal Outstanding,
    double SharePercent,
    double CumulativePercent);

/// <summary>
/// Turns per-payee totals into a Pareto ranking — biggest debt first, with a
/// running share of the whole.
/// <para>
/// Here rather than in the component because it is arithmetic with edge cases
/// (an empty range, a fully-paid range, ties) and those are worth testing
/// without a renderer in the way.
/// </para>
/// </summary>
public static class ParetoRows
{
    /// <summary>
    /// The share the headline sentence counts payees up to. Sixty percent is
    /// the point where "a few payees are most of the problem" stops being a
    /// fair reading of the data, so it is where the sentence stops counting.
    /// </summary>
    public const double HeadlineThreshold = 60d;

    /// <summary>
    /// Ranks the payees who are still owed something.
    /// <para>
    /// The total is the sum of the rows that survive the filter, not
    /// <see cref="BillSummary.OutstandingAmount"/>. They are the same number,
    /// but deriving it here is what makes the last row exactly 100% —
    /// borrowing a separately-computed total would leave the column ending on
    /// 99.8% whenever the two rounded differently.
    /// </para>
    /// </summary>
    public static List<ParetoRow> Build(IEnumerable<PayeeTotals>? payees)
    {
        var owing = (payees ?? Enumerable.Empty<PayeeTotals>())
            .Where(p => p.Outstanding > 0m)
            .OrderByDescending(p => p.Outstanding)
            .ThenBy(p => p.Payee, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = owing.Sum(p => p.Outstanding);

        if (total <= 0m)
        {
            return new List<ParetoRow>();
        }

        var rows = new List<ParetoRow>(owing.Count);
        var running = 0m;

        foreach (var payee in owing)
        {
            running += payee.Outstanding;

            rows.Add(new ParetoRow(
                Payee: payee.Payee,
                Bills: payee.Bills,
                Outstanding: payee.Outstanding,
                SharePercent: (double)(payee.Outstanding / total) * 100d,
                CumulativePercent: (double)(running / total) * 100d));
        }

        return rows;
    }

    /// <summary>
    /// How many payees from the top it takes to account for
    /// <paramref name="percent"/> of the total.
    /// </summary>
    /// <returns>
    /// Zero for an empty ranking. Every row if the threshold is never crossed
    /// — which only happens for a threshold above 100, but returning the whole
    /// count is the honest answer either way: that is how many payees it took.
    /// </returns>
    public static int PayeesToReach(IReadOnlyList<ParetoRow> rows, double percent)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].CumulativePercent >= percent)
            {
                return i + 1;
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// The section's framing sentence, or null when there is nothing to frame.
    /// </summary>
    public static string? Headline(IReadOnlyList<ParetoRow> rows, double threshold = HeadlineThreshold)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var count = PayeesToReach(rows, threshold);
        var share = rows[count - 1].CumulativePercent;

        // Invariant on the percent for the same reason the widths are: this is
        // a whole number by the time it is formatted, and a culture that
        // groups digits differently would not change it — but pinning it keeps
        // the sentence identical on every machine that renders it.
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} for {3:0}% of everything you owe.",
            NumberWords.Spell(count),
            count == 1 ? "payee" : "payees",
            count == 1 ? "accounts" : "account",
            share);
    }
}
