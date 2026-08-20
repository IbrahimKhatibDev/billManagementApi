using System.Globalization;
using System.Text.RegularExpressions;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.Parsing;

/// <summary>
/// Reads "Verizon 89.20 fri" as payee, amount and due date.
/// <para>
/// The grammar is positional and deliberately small: the payee is everything up
/// to the first number, the amount is that number, and the date is whatever
/// follows it. Anything outside the grammar comes back as a partial reading with
/// <see cref="ParseConfidence.Low"/> rather than an error — the client shows the
/// reading and lets the user fix it, so a miss costs a keystroke, not a failure.
/// </para>
/// </summary>
public static partial class BillTextParser
{
    private static readonly string[] Prepositions = { "due", "on", "by", "at" };

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sun"] = DayOfWeek.Sunday,
        ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday,
        ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["tues"] = DayOfWeek.Tuesday,
        ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["weds"] = DayOfWeek.Wednesday,
        ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["thur"] = DayOfWeek.Thursday,
        ["thurs"] = DayOfWeek.Thursday,
        ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["saturday"] = DayOfWeek.Saturday,
    };

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1,
        ["feb"] = 2, ["february"] = 2,
        ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4,
        ["may"] = 5,
        ["jun"] = 6, ["june"] = 6,
        ["jul"] = 7, ["july"] = 7,
        ["aug"] = 8, ["august"] = 8,
        ["sep"] = 9, ["sept"] = 9, ["september"] = 9,
        ["oct"] = 10, ["october"] = 10,
        ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["december"] = 12,
    };

    // The lookarounds are what keep "8/21" out of the amount: a digit touching a
    // slash or a dot on either side is part of something else.
    [GeneratedRegex(@"(?<![\d./])\$?(?<amount>\d+(?:\.\d{1,2})?)(?![\d./])")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{2}|\d{4}))?$")]
    private static partial Regex NumericDatePattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z]+)\.?\s+(?<day>\d{1,2})$")]
    private static partial Regex MonthFirstPattern();

    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<name>[A-Za-z]+)\.?$")]
    private static partial Regex DayFirstPattern();

    /// <param name="today">
    /// Midnight UTC of the day the request arrived. Passed in rather than read
    /// from a clock so "fri" is a testable answer.
    /// </param>
    public static ParsedBill Parse(string? text, DateTime today)
    {
        var parsed = new ParsedBill { Confidence = ParseConfidence.Low };

        if (string.IsNullOrWhiteSpace(text))
        {
            return parsed;
        }

        var input = text.Trim();
        var amount = AmountPattern().Match(input);

        if (!amount.Success)
        {
            // No number anywhere: the line is a payee and nothing else.
            parsed.Payee = Clean(input);
            return parsed;
        }

        parsed.Payee = Clean(input[..amount.Index]);
        parsed.Amount = decimal.Parse(
            amount.Groups["amount"].Value, CultureInfo.InvariantCulture);

        if (TryResolveDate(input[(amount.Index + amount.Length)..], today, out var due))
        {
            parsed.DueDate = due;
        }

        if (!string.IsNullOrEmpty(parsed.Payee) && parsed.DueDate is not null)
        {
            parsed.Confidence = ParseConfidence.High;
        }

        return parsed;
    }

    private static string? Clean(string value)
    {
        var trimmed = value.Trim().Trim('-', ',', ':', ';').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool TryResolveDate(string phrase, DateTime today, out DateTime due)
    {
        due = default;

        var words = phrase
            .Trim()
            .Trim(',', '.', ';', ':', '-')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var start = 0;
        while (start < words.Length
               && Prepositions.Contains(words[start], StringComparer.OrdinalIgnoreCase))
        {
            start++;
        }

        var cleaned = string.Join(' ', words.Skip(start));
        if (cleaned.Length == 0)
        {
            return false;
        }

        if (string.Equals(cleaned, "today", StringComparison.OrdinalIgnoreCase))
        {
            due = today.Date;
            return true;
        }

        if (string.Equals(cleaned, "tomorrow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cleaned, "tmrw", StringComparison.OrdinalIgnoreCase))
        {
            due = today.Date.AddDays(1);
            return true;
        }

        if (Weekdays.TryGetValue(cleaned, out var weekday))
        {
            // Strictly forward, 1 to 7 days out.
            var delta = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
            due = today.Date.AddDays(delta == 0 ? 7 : delta);
            return true;
        }

        var numeric = NumericDatePattern().Match(cleaned);
        if (numeric.Success)
        {
            int? year = numeric.Groups["y"].Success
                ? NormalizeYear(int.Parse(numeric.Groups["y"].Value, CultureInfo.InvariantCulture))
                : null;

            return TryBuild(
                int.Parse(numeric.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(numeric.Groups["d"].Value, CultureInfo.InvariantCulture),
                year,
                today,
                out due);
        }

        var monthFirst = MonthFirstPattern().Match(cleaned);
        if (monthFirst.Success && Months.TryGetValue(monthFirst.Groups["name"].Value, out var month))
        {
            return TryBuild(
                month,
                int.Parse(monthFirst.Groups["day"].Value, CultureInfo.InvariantCulture),
                null,
                today,
                out due);
        }

        var dayFirst = DayFirstPattern().Match(cleaned);
        if (dayFirst.Success && Months.TryGetValue(dayFirst.Groups["name"].Value, out month))
        {
            return TryBuild(
                month,
                int.Parse(dayFirst.Groups["day"].Value, CultureInfo.InvariantCulture),
                null,
                today,
                out due);
        }

        return false;
    }

    private static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    private static bool TryBuild(int month, int day, int? year, DateTime today, out DateTime due)
    {
        due = default;

        if (month is < 1 or > 12)
        {
            return false;
        }

        var resolved = year ?? today.Year;
        if (day < 1 || day > DateTime.DaysInMonth(resolved, month))
        {
            return false;
        }

        var candidate = new DateTime(resolved, month, day, 0, 0, 0, DateTimeKind.Utc);

        // "mar 3" typed in August means next March. Only a year the user did not
        // supply may be rolled forward.
        if (year is null && candidate < today.Date)
        {
            if (day > DateTime.DaysInMonth(resolved + 1, month))
            {
                return false;
            }

            candidate = new DateTime(resolved + 1, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        due = candidate;
        return true;
    }
}
