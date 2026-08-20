using System.Globalization;

namespace BillsMinimalApi.Contracts;

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
    public static string Describe(BillSummary summary, IFormatProvider? formatProvider = null)
    {
        var money = formatProvider ?? CultureInfo.CurrentCulture;

        if (summary.OutstandingAmount <= 0)
        {
            return "Nothing outstanding — every bill on the books is paid.";
        }

        var late = summary.OverdueAmount > 0
            ? string.Format(
                money,
                "{0:C} of it is already late, spread across {1} — the oldest by {2}.",
                summary.OverdueAmount,
                Bills(summary.OverdueCount),
                Days(summary.OldestDaysLate))
            : "None of it is late.";

        return $"{late} {Coming(summary, money)}";
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
