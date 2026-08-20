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
    // slash, a dot or a comma on either side is part of something else.
    //
    // The grouped-thousands alternative is written first because .NET alternation
    // is ordered and does not prefer the longer match. With "\d+" leading,
    // "1,299.50" matched the "1" and stopped — the comma satisfied the trailing
    // lookaround — and a bill for $1,299.50 was read as one for $1.00.
    [GeneratedRegex(@"(?<![\d.,/])\$?(?<amount>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)(?![\d./])")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{2}|\d{4}))?$")]
    private static partial Regex NumericDatePattern();

    // The year is optional and the comma before it is too, so "aug 18",
    // "aug 18 2027" and "Aug 18, 2027" are all the same date grammar. Without
    // the year group a spelled-out month could only ever mean this year or the
    // next one, while the slashed form has taken a year since it was written —
    // "aug 18 2027" simply came back dateless.
    //
    // \d{2}|\d{4} in that order is fine even though .NET alternation is
    // ordered: "2027" matches \d{2} as "20", then fails the anchor and
    // backtracks into \d{4}. NumericDatePattern above has always relied on it.
    [GeneratedRegex(@"^(?<name>[A-Za-z]+)\.?\s+(?<day>\d{1,2}),?(?:\s+(?<year>\d{2}|\d{4}))?$")]
    private static partial Regex MonthFirstPattern();

    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<name>[A-Za-z]+)\.?,?(?:\s+(?<year>\d{2}|\d{4}))?$")]
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

        // TryParse rather than Parse. The pattern bounds the *shape* of the
        // number but not its length, and decimal.Parse throws OverflowException
        // past ~7.9e28. The parse endpoint returns the reading directly with no
        // exception handler, so a pasted 30-digit account number came back as a
        // 500 on every debounced keystroke while the chips silently vanished.
        // Unparseable leaves Amount null, which is a reading the user can fix.
        //
        // NumberStyles.Number is what accepts the thousands separators the
        // pattern now matches whole.
        if (decimal.TryParse(
                amount.Groups["amount"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value))
        {
            parsed.Amount = value;
        }

        if (TryResolveDate(input[(amount.Index + amount.Length)..], today, out var due))
        {
            parsed.DueDate = due;
        }

        // All three fields, which is what ParsedBill.Confidence documents. The
        // amount is held to the same floor the client's IsComplete and the DTO's
        // [Range] both enforce: "Gas 0 fri" resolved a payee and a date and so
        // used to come back high-confidence next to an Add button that refused
        // it. A number too long to parse also lands here as Low, correctly.
        if (!string.IsNullOrEmpty(parsed.Payee)
            && parsed.DueDate is not null
            && parsed.Amount >= InlineEditValues.MinimumAmount)
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

        // "Aug 21" and "21 aug" are the same grammar with the month and day
        // swapped, so one loop over both patterns replaces two copies of the
        // same match-then-build logic. Month-first is tried before day-first.
        foreach (var pattern in new[] { MonthFirstPattern(), DayFirstPattern() })
        {
            var m = pattern.Match(cleaned);
            if (m.Success && Months.TryGetValue(m.Groups["name"].Value, out var month))
            {
                // Null when the user did not type one, which is what makes a
                // month already gone roll into next year. A year they did type
                // is theirs, past or not — the same rule the slashed form uses.
                int? year = m.Groups["year"].Success
                    ? NormalizeYear(int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture))
                    : null;

                return TryBuild(
                    month,
                    int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture),
                    year,
                    today,
                    out due);
            }
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
