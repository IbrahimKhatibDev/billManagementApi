using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// The bill-size distribution as one sentence rather than five rows.
/// <para>
/// The table it replaces answered "how are my bills distributed by size",
/// which nobody asks. The question underneath it — "where is the money" — has
/// a one-line answer, so that is what this returns: the band holding the most
/// money, how many bills are in it, and what share of the total it is.
/// </para>
/// </summary>
public static class SizeBandSentence
{
    /// <summary>The separator <c>BillSummaryBuilder.SizeBandLabels</c> uses
    /// between the two ends of a range. An en dash, not a hyphen.</summary>
    private const char EnDash = '–';

    private const string UnderPrefix = "Under ";

    private const string OverSuffix = " and over";

    /// <summary>
    /// Names the band holding the most money, or null when there is nothing to
    /// say — no bands, no bills, or no money in any of them.
    /// </summary>
    public static string? Describe(IReadOnlyList<SizeBand>? bands)
    {
        if (bands is null || bands.Count == 0)
        {
            return null;
        }

        var billCount = bands.Sum(b => b.Count);
        var money = bands.Sum(b => b.Total);

        if (billCount == 0 || money <= 0m)
        {
            return null;
        }

        // OrderByDescending is stable over a list, so a tie keeps the server's
        // order — which runs smallest band to largest. Naming the smaller of
        // two equally expensive bands is the more surprising fact of the two,
        // and surprise is the whole point of the sentence.
        var top = bands.OrderByDescending(b => b.Total).First();
        var phrase = Phrase(top.Label);

        if (billCount == 1)
        {
            return $"The only bill in this range is {phrase}.";
        }

        // "Twelve of 12 bills" is a sentence that makes a reader stop and
        // check, for no gain — and the money share is trivially 100%.
        if (top.Count == billCount)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "All {0} bills sit {1}.",
                billCount,
                phrase);
        }

        var share = (double)(top.Total / money) * 100d;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} of {1} bills sit {2} — that band is {3:0}% of the money.",
            NumberWords.Spell(top.Count),
            billCount,
            phrase,
            share);
    }

    /// <summary>
    /// Turns a band label into something that reads inside a sentence:
    /// <c>$250 – $499</c> becomes <c>between $250 and $499</c>.
    /// </summary>
    /// <remarks>
    /// Shape-matched rather than label-matched, so rewording a band server-side
    /// degrades to "at &lt;whatever it now says&gt;" instead of falling through
    /// to a case that no longer exists.
    /// </remarks>
    public static string Phrase(string? label)
    {
        var text = (label ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return "in that band";
        }

        var dash = text.IndexOf(EnDash);

        if (dash > 0)
        {
            var low = text[..dash].Trim();
            var high = text[(dash + 1)..].Trim();

            return $"between {low} and {high}";
        }

        if (text.StartsWith(UnderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "under " + text[UnderPrefix.Length..];
        }

        return "at " + text.Replace(OverSuffix, " or more", StringComparison.OrdinalIgnoreCase);
    }
}
