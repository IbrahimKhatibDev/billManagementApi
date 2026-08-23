namespace BillsMinimalApi.Contracts;

/// <summary>Which section of the Bills page a bill belongs to.</summary>
public enum DueWindow
{
    Late,
    ThisWeek,
    ThisMonth,
    Later,
    Paid,
}

/// <summary>
/// The grammar behind the Bills page's five sections.
/// <para>
/// In the contracts project rather than in the page, because the page cannot be
/// unit tested — and because these five predicates have to stay mutually
/// exclusive and exhaustive, which is a property worth asserting rather than
/// eyeballing.
/// </para>
/// </summary>
public static class DueWindows
{
    /// <summary>Reading order: the thing that needs doing first is first.</summary>
    public static IReadOnlyList<DueWindow> Order { get; } = new[]
    {
        DueWindow.Late,
        DueWindow.ThisWeek,
        DueWindow.ThisMonth,
        DueWindow.Later,
        DueWindow.Paid,
    };

    public static string Title(DueWindow window) => window switch
    {
        DueWindow.Late => "Late",
        DueWindow.ThisWeek => "Due this week",
        DueWindow.ThisMonth => "Due this month",
        DueWindow.Later => "Later",
        _ => "Paid",
    };

    /// <summary>
    /// The Sunday that closes the week <paramref name="today"/> falls in.
    /// <para>
    /// Built from <see cref="WeekBuckets.StartOfWeek"/> so the Bills page and the
    /// Overview's timeline agree about where a week begins. The design prototype
    /// computed this as <c>today + (7 - dayOfWeek)</c>, which returns next Sunday
    /// when today is a Sunday — a whole extra week of bills described as due this
    /// one.
    /// </para>
    /// </summary>
    public static DateTime EndOfWeek(DateTime today) =>
        WeekBuckets.StartOfWeek(today).AddDays(6);

    public static DateTime EndOfMonth(DateTime today) =>
        new(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

    /// <summary>
    /// Which section a bill belongs to. Ordered so the first match wins: paid
    /// beats every date, and late beats every deadline.
    /// </summary>
    /// <param name="dueDate">
    /// Null lands in <see cref="DueWindow.Later"/>. The API always sends a due
    /// date; the client's model allows null so its create form can fail
    /// validation rather than throw.
    /// </param>
    public static DueWindow Classify(bool paid, DateTime? dueDate, DateTime today)
    {
        if (paid)
        {
            return DueWindow.Paid;
        }

        if (dueDate is not { } due)
        {
            return DueWindow.Later;
        }

        // Dates, not instants. Due dates are stored at midnight UTC and a bill
        // edited through the form can carry a local time; comparing the whole
        // value would make "due today" read as late by mid-morning.
        var day = due.Date;
        var now = today.Date;

        if (day < now)
        {
            return DueWindow.Late;
        }

        if (day <= EndOfWeek(now))
        {
            return DueWindow.ThisWeek;
        }

        // Checked after the week deliberately: in the last days of a month the
        // week runs past the month end, and a bill three days out is this week's
        // problem rather than one to defer to Later.
        return day <= EndOfMonth(now) ? DueWindow.ThisMonth : DueWindow.Later;
    }
}
