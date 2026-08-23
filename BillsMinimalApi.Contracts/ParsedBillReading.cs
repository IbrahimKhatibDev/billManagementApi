using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// Reads a <see cref="ParsedBill"/> back to the person who typed it: three
/// chips, and whether they add up to something worth posting.
/// <para>
/// Separate from <see cref="ParseConfidence"/> on purpose. Confidence is the
/// server's report on its own parse and never changes; this is asked again every
/// time the user corrects a chip, and a low-confidence parse that has since been
/// filled in by hand is perfectly postable.
/// </para>
/// </summary>
public static class ParsedBillReading
{
    /// <summary>Shown in place of a piece the parser did not find. Written as an
    /// instruction because the chip is clickable — it is the fix, not just the
    /// complaint.</summary>
    public const string MissingPayee = "add a payee";

    public const string MissingAmount = "add an amount";

    public const string MissingDate = "add a date";

    /// <summary>
    /// Whether this reading would survive <c>POST /restapi/BillDtos</c>. The
    /// amount floor is <see cref="InlineEditValues.MinimumAmount"/> rather than
    /// "not null", because the parser takes the first number in the line and zero
    /// is a number.
    /// </summary>
    public static bool IsComplete(ParsedBill? reading) =>
        reading is not null
        && !string.IsNullOrWhiteSpace(reading.Payee)
        && reading.Amount >= InlineEditValues.MinimumAmount
        && reading.DueDate is not null;

    public static string PayeeText(ParsedBill? reading)
    {
        var payee = reading?.Payee?.Trim();
        return string.IsNullOrEmpty(payee) ? MissingPayee : payee;
    }

    public static string AmountText(ParsedBill? reading, IFormatProvider? formats = null) =>
        reading?.Amount is { } amount
            ? amount.ToString("C", formats ?? CultureInfo.CurrentCulture)
            : MissingAmount;

    public static string DueText(ParsedBill? reading, IFormatProvider? formats = null) =>
        reading?.DueDate is { } due
            ? due.ToString("MMM d, yyyy", formats ?? CultureInfo.CurrentCulture)
            : MissingDate;
}
