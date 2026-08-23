using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// Small counts written as words, for sentences that open on one.
/// <para>
/// "Three payees account for…" reads as prose; "3 payees account for…" reads
/// as a log line. Above twenty the word is longer than the number and stops
/// helping, so the rule flips — which is the convention most style guides
/// land on and, more to the point, the one the design's own copy follows.
/// </para>
/// </summary>
public static class NumberWords
{
    /// <summary>The last count that gets a word rather than digits.</summary>
    public const int LargestSpelled = 20;

    private static readonly string[] Words =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven",
        "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen",
        "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty",
    };

    /// <summary>
    /// Capitalised, because every caller uses this at the start of a sentence.
    /// A caller needing it mid-sentence would lower-case the first letter
    /// itself rather than this growing a second overload nobody has asked for.
    /// </summary>
    public static string Spell(int count) =>
        count is >= 0 and <= LargestSpelled
            ? Words[count]
            : count.ToString(CultureInfo.InvariantCulture);
}
