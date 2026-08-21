using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// The sentence the Overview leads with, cut at the one clause that gets
/// coloured — the money already late.
/// <para>
/// <see cref="Rest"/> opens with the punctuation that follows the clause, so the
/// two concatenate with nothing between them. That is what lets the component
/// wrap <see cref="Late"/> in a span without Razor's indentation landing as a
/// space in front of a comma.
/// </para>
/// <para>
/// <see cref="Late"/> is empty whenever there is no late money to name — an
/// account that is settled, or one whose bills are all still ahead of it. There
/// is nothing to highlight in either case, and the whole sentence is in
/// <see cref="Rest"/>.
/// </para>
/// </summary>
public readonly record struct ObligationParts(string Late, string Rest)
{
    public override string ToString() => Late + Rest;
}

/// <summary>
/// The sentence the Overview leads with, built from figures the server already
/// sends.
/// <para>
/// It lives here rather than in the component because it is a decision tree over
/// five numbers with pluralisation in it, and a decision tree in markup is a
/// decision tree nobody can test. This assembly is the only one both the Blazor
/// app and the unit tests reference, which is what makes that possible.
/// </para>
/// </summary>
public static class ObligationSentence
{
    /// <param name="formatProvider">
    /// How to spell money. Defaults to the current culture, which the app pins to
    /// en-US in <c>Program.cs</c>; tests pass it explicitly so they do not depend
    /// on the machine they run on.
    /// </param>
    public static string Describe(BillSummary summary, IFormatProvider? formatProvider = null) =>
        DescribeParts(summary, formatProvider).ToString();

    /// <summary>
    /// The same sentence, split at the clause the Overview colours. Building the
    /// whole from the parts rather than the other way round is what keeps the
    /// coloured version and the plain one from ever drifting: there is one
    /// decision tree, and <see cref="Describe"/> is a join.
    /// </summary>
    /// <param name="formatProvider">See <see cref="Describe"/>.</param>
    public static ObligationParts DescribeParts(
        BillSummary summary, IFormatProvider? formatProvider = null)
    {
        var money = formatProvider ?? CultureInfo.CurrentCulture;

        if (summary.OutstandingAmount <= 0)
        {
            return new ObligationParts(
                string.Empty, "Nothing outstanding — every bill on the books is paid.");
        }

        if (summary.OverdueAmount <= 0)
        {
            return new ObligationParts(string.Empty, $"None of it is late. {Coming(summary, money)}");
        }

        // The cut falls before the comma: the figure and the claim about it are
        // what the colour is for, and "spread across 8 bills" is detail that
        // reads better in the body colour beside it.
        return new ObligationParts(
            string.Format(money, "{0:C} of it is already late", summary.OverdueAmount),
            $", spread across {Bills(summary.OverdueCount)} — "
            + $"the oldest by {Days(summary.OldestDaysLate)}. {Coming(summary, money)}");
    }

    private static string Coming(BillSummary summary, IFormatProvider money)
    {
        if (summary.DueSoonAmount <= 0)
        {
            return $"Nothing falls due inside the next {BillSummary.DueSoonDays} days.";
        }

        // "The rest" is a claim about the total printed directly above this
        // sentence, so it is only made when the two windows actually account for
        // all of it. Anything due beyond the window makes it false.
        if (summary.OverdueAmount > 0)
        {
            var coversEverything =
                summary.OverdueAmount + summary.DueSoonAmount == summary.OutstandingAmount;

            return string.Format(
                money,
                coversEverything
                    ? "The rest, {0:C}, falls due inside the next {1} days."
                    : "{0:C} of the remainder falls due inside the next {1} days.",
                summary.DueSoonAmount,
                BillSummary.DueSoonDays);
        }

        // Nothing is late, so the due-soon money is not "the rest" of anything —
        // it is simply what is coming.
        return string.Format(
            money,
            "{0:C} falls due inside the next {1} days.",
            summary.DueSoonAmount,
            BillSummary.DueSoonDays);
    }

    private static string Bills(int count) => count == 1 ? "1 bill" : $"{count} bills";

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}
