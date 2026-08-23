namespace BillsMinimalApi.Contracts;

/// <summary>
/// Everything the Reports page renders, computed in Postgres.
/// <para>
/// One response rather than several endpoints because every section on that
/// page has to describe the same window as the others. Splitting it up would
/// let the headline figures and the month table be computed a second apart,
/// which across midnight — or across a concurrent write — is long enough for
/// them to disagree. <see cref="AsOf"/> is the date the whole response was
/// computed against, sent back so the client renders the server's idea of
/// "today" rather than its own.
/// </para>
/// </summary>
public sealed class BillSummary
{
    /// <summary>How far ahead "due soon" looks. Here rather than in the UI
    /// because the server is what counts the bills.</summary>
    public const int DueSoonDays = 30;

    /// <summary>How many bills the "what to pay next" shortlist holds. It is a
    /// shortlist, not a second bills page.</summary>
    public const int PriorityCount = 6;

    /// <summary>
    /// How long a run of weeks the cash-flow timeline will draw before it gives
    /// up on being continuous. Five years of columns is already more than a
    /// screen holds; past that the span is a typo, not a plan.
    /// </summary>
    public const int MaxWeeks = 260;

    /// <summary>
    /// How many late bills the triage list carries. A list you work through, not
    /// a second bills table — and a bound on a response that would otherwise
    /// grow with the size of the problem. The Overview reports
    /// <see cref="OverdueCount"/> alongside it, so a truncated list says so.
    /// </summary>
    public const int LateCount = 200;

    public DateTime AsOf { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    // -- Headline figures ---------------------------------------------------

    public int BillCount { get; set; }

    public decimal TotalBilled { get; set; }

    public decimal PaidAmount { get; set; }

    public int UnpaidCount { get; set; }

    public int OverdueCount { get; set; }

    public decimal OverdueAmount { get; set; }

    public int DueSoonCount { get; set; }

    public decimal DueSoonAmount { get; set; }

    public decimal AverageBill { get; set; }

    public decimal MedianBill { get; set; }

    public SummaryBill? LargestBill { get; set; }

    /// <summary>Derived rather than sent, so it cannot contradict the two
    /// figures it is the difference of.</summary>
    public decimal OutstandingAmount => TotalBilled - PaidAmount;

    /// <summary>
    /// Share of money settled, not of bills — ten small paid bills and one
    /// large unpaid one is not a 91% healthy picture.
    /// </summary>
    public double PaidPercent => TotalBilled == 0
        ? 0
        : (double)(PaidAmount / TotalBilled) * 100;

    // -- Breakdowns ---------------------------------------------------------

    /// <summary>The five fixed aging buckets, always all five and always in
    /// order. An empty "over 90 days" row is information, and a table that
    /// grows and shrinks as you change the range is hard to read across
    /// presets.</summary>
    public List<AgingBucket> Aging { get; set; } = new();

    public List<PayeeTotals> Payees { get; set; } = new();

    /// <summary>Newest month first: the months you are being asked about now
    /// are the recent ones, and they should not be at the bottom of a
    /// scroll.</summary>
    public List<MonthTotals> Months { get; set; } = new();

    /// <summary>
    /// Every week the book touches, oldest first, with paid and unpaid kept
    /// apart so the column can stack. Weeks with nothing in them are included:
    /// same argument as <see cref="Aging"/> — a gap in the cash flow is what the
    /// chart is for.
    /// </summary>
    public List<WeekTotals> Weeks { get; set; } = new();

    /// <summary>The five fixed size bands, same contract as
    /// <see cref="Aging"/>.</summary>
    public List<SizeBand> SizeBands { get; set; } = new();

    /// <summary>The unpaid bills to deal with first: latest first, then
    /// whatever is due soonest.</summary>
    public List<SummaryBill> Priority { get; set; } = new();

    /// <summary>
    /// Every unpaid bill already past due, oldest first — capped at
    /// <see cref="LateCount"/>. Distinct from <see cref="Priority"/>, which is a
    /// six-bill shortlist that also includes bills merely due soon.
    /// </summary>
    public List<SummaryBill> Late { get; set; } = new();

    /// <summary>
    /// How many days late the worst bill is, or 0 when nothing is late.
    /// <para>
    /// Derived rather than queried: <see cref="Late"/> is already ordered by due
    /// date, so its first row is the oldest one. A second query could disagree
    /// with the list it is printed next to.
    /// </para>
    /// </summary>
    public int OldestDaysLate => Late.Count == 0 ? 0 : Late[0].DaysLate;
}

/// <summary>
/// A bill as a report cites it. Deliberately not <c>BillDto</c>: this is a
/// read-only mention on a page that cannot edit anything, so it carries no
/// concurrency token to be mistaken for one that could be written back — and it
/// carries <see cref="DaysLate"/>, which only the server can compute correctly
/// because only the server knows which date the rest of the response used.
/// </summary>
public sealed class SummaryBill
{
    public long Id { get; set; }

    public string PayeeName { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public decimal PaymentDue { get; set; }

    /// <summary>Days past due, or 0 for anything not yet due.</summary>
    public int DaysLate { get; set; }
}

public sealed class AgingBucket
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public decimal Amount { get; set; }
}

public sealed class PayeeTotals
{
    public string Payee { get; set; } = string.Empty;

    public int Bills { get; set; }

    public decimal Billed { get; set; }

    public decimal Paid { get; set; }

    public decimal Outstanding => Billed - Paid;
}

/// <summary>
/// Year and month rather than a formatted label: how "March 2026" is spelled is
/// the client's business, and a server that sent the string would decide the
/// culture for it.
/// </summary>
public sealed class MonthTotals
{
    public int Year { get; set; }

    public int Month { get; set; }

    public int Bills { get; set; }

    public decimal Billed { get; set; }

    public decimal Paid { get; set; }

    public decimal Outstanding => Billed - Paid;

    public double PaidPercent => Billed == 0 ? 0 : (double)(Paid / Billed) * 100;

    public DateTime FirstDay => new(Year, Month, 1);
}

/// <summary>
/// One column of the cash-flow timeline. Monday-start, because a week that
/// begins on Sunday puts two of a month's paydays in the same column.
/// </summary>
public sealed class WeekTotals
{
    /// <summary>Monday of the week, midnight UTC.</summary>
    public DateTime WeekStart { get; set; }

    public int Bills { get; set; }

    public decimal Paid { get; set; }

    public decimal Unpaid { get; set; }

    public decimal Total => Paid + Unpaid;
}

public sealed class SizeBand
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public decimal Total { get; set; }
}
