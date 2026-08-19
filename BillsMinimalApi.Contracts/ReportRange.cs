namespace BillsMinimalApi.Contracts;

/// <summary>
/// The date windows the Reports page offers. Presets rather than a pair of
/// date pickers: every section on the page has to agree on one window, and a
/// free-form range gives you a hundred ways to ask a question nobody asks.
/// </summary>
public enum ReportRange
{
    AllTime,
    ThisYear,
    Last6Months,
    Last3Months,
    Next3Months,
}

/// <summary>
/// Window arithmetic for <see cref="ReportRange"/>.
/// <para>
/// This lives in the contracts project rather than in the UI because three
/// places now have to agree on what a preset covers: the Reports page, the CSV
/// endpoint it exports from, and the API's own <c>from</c>/<c>to</c> filtering.
/// A second copy on the server would be a copy that eventually disagrees, and
/// the symptom — an export covering a different set of bills than the page it
/// came from — is the kind that goes unnoticed.
/// </para>
/// </summary>
public static class ReportRanges
{
    public static IReadOnlyList<ReportRange> All { get; } = Enum.GetValues<ReportRange>();

    public static string Label(this ReportRange range) => range switch
    {
        ReportRange.ThisYear => "This year",
        ReportRange.Last6Months => "Last 6 months",
        ReportRange.Last3Months => "Last 3 months",

        // Forward-looking, and not symmetry for its own sake: bills are
        // mostly due in the future, so a page that only ever looks backwards
        // hides the ones you still have to pay.
        ReportRange.Next3Months => "Next 3 months",
        _ => "All time",
    };

    /// <summary>Used in the CSV endpoint's query string and filename, where
    /// "Last6Months" would be an odd thing to see in a Downloads folder.
    /// </summary>
    public static string Slug(this ReportRange range) => range switch
    {
        ReportRange.ThisYear => "this-year",
        ReportRange.Last6Months => "last-6-months",
        ReportRange.Last3Months => "last-3-months",
        ReportRange.Next3Months => "next-3-months",
        _ => "all-time",
    };

    /// <summary>
    /// Accepts either the slug or the enum name, and falls back to
    /// <see cref="ReportRange.AllTime"/> rather than throwing — this parses a
    /// query string, which anyone can type anything into.
    /// </summary>
    public static ReportRange Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ReportRange.AllTime;
        }

        foreach (var range in All)
        {
            if (string.Equals(value, range.Slug(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, range.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return range;
            }
        }

        return ReportRange.AllTime;
    }

    /// <summary>
    /// The inclusive window a range covers, or nulls for "no bound". Both
    /// ends are whole dates: due dates carry a time component from the API
    /// (midnight UTC), so every comparison here is on <c>.Date</c>.
    /// </summary>
    public static (DateTime? From, DateTime? To) Window(this ReportRange range, DateTime today) => range switch
    {
        ReportRange.ThisYear => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
        ReportRange.Last6Months => (today.AddMonths(-6), today),
        ReportRange.Last3Months => (today.AddMonths(-3), today),
        ReportRange.Next3Months => (today, today.AddMonths(3)),
        _ => (null, null),
    };

    public static bool Includes(this ReportRange range, DateTime today, DateTime? dueDate)
    {
        var (from, to) = range.Window(today);

        if (from is null && to is null)
        {
            return true;
        }

        // A bill with no due date cannot be placed on a timeline, so any
        // bounded window drops it. All time keeps it, which is why the page
        // says how many were left out whenever a preset is active.
        if (dueDate is not { } due)
        {
            return false;
        }

        var date = due.Date;

        return (from is null || date >= from.Value.Date)
               && (to is null || date <= to.Value.Date);
    }

    /// <summary>One line under the preset row saying exactly what is on
    /// screen, so no preset has to be guessed at.</summary>
    public static string Caption(this ReportRange range, DateTime today)
    {
        var (from, to) = range.Window(today);

        if (from is null || to is null)
        {
            return "Every bill, whenever it is due.";
        }

        return $"Bills due between {from.Value:MMM d, yyyy} and {to.Value:MMM d, yyyy}.";
    }
}
