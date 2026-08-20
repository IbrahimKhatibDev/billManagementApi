using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>Which of the three editable fields a cell is standing in for.</summary>
public enum InlineEditKind
{
    Text,
    Date,
    Amount,
}

/// <summary>
/// Reads the three values a row can be edited to, straight out of what the input
/// element sends.
/// <para>
/// In the contracts project because it is the only part of inline editing a unit
/// test can reach, and it is the part where being wrong is expensive: there is no
/// <c>EditForm</c> in front of these values, so whatever they return goes into a
/// PUT.
/// </para>
/// </summary>
public static class InlineEditValues
{
    /// <summary>
    /// The smallest amount the API will accept, from the
    /// <c>[Range(0.01, double.MaxValue)]</c> on <c>BillDto.PaymentDue</c>.
    /// Duplicated here on purpose: the alternative is letting the field commit a
    /// value the server is certain to refuse.
    /// </summary>
    public const decimal MinimumAmount = 0.01m;

    /// <summary>
    /// Reads <c>yyyy-MM-dd</c> — the only form <c>input type="date"</c> sends,
    /// whatever the browser displays.
    /// </summary>
    public static bool TryParseDate(string? raw, out DateTime value)
    {
        value = default;

        if (!DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        // Utc, not Unspecified. This travels to the API and on to a
        // `timestamp with time zone` column, and Npgsql throws on any other
        // kind — a failure that would surface as a 500 rather than as a field
        // that declines to commit.
        value = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        return true;
    }

    /// <summary>
    /// Reads what <c>input type="number"</c> sends: a plain invariant number,
    /// dot-separated, optionally signed. No thousands separators, because the
    /// element never produces one.
    /// </summary>
    public static bool TryParseAmount(string? raw, out decimal value)
    {
        value = 0m;

        if (!decimal.TryParse(
                raw,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        // Before the rounding, not after: anything under a cent is refused as
        // typed rather than rounded into one. So a fraction of a cent cannot
        // reach the round at all, and nothing that does can fall back below the
        // floor — a value of 0.01 or more still rounds to 0.01 or more.
        if (parsed < MinimumAmount)
        {
            return false;
        }

        value = decimal.Round(parsed, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Trims the payee and refuses a blank one, matching the DTO's
    /// <c>[Required]</c>.
    /// </summary>
    public static bool TryParsePayee(string? raw, out string value)
    {
        value = raw?.Trim() ?? string.Empty;
        return value.Length > 0;
    }
}
