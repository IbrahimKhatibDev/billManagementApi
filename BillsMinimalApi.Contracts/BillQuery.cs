namespace BillsMinimalApi.Contracts;

/// <summary>
/// Overdue is a peer of Unpaid rather than a separate axis, even though it is a
/// strict subset of it: this answers one question — "which bills am I looking
/// at?" — and two independent controls would let you ask for Paid + Overdue,
/// which is always empty.
/// </summary>
public enum BillStatus
{
    All,
    Paid,
    Unpaid,
    Overdue,
}

public enum BillSort
{
    Id,
    Payee,
    DueDate,
    Amount,
    Paid,
}

/// <summary>
/// Everything <c>GET /restapi/BillDtos</c> accepts, and the parsing that turns a
/// query string into it.
/// <para>
/// Every parser falls back to a default instead of throwing. These values come
/// straight off a URL that anyone can type anything into, and a 400 for
/// <c>?sort=dueDate&amp;dir=descending</c> helps nobody — the same reasoning as
/// <see cref="ReportRanges.Parse"/>.
/// </para>
/// </summary>
public sealed record BillQuery(
    int Page,
    int PageSize,
    string? Search,
    BillStatus Status,
    BillSort Sort,
    bool Descending,
    DateTime? From,
    DateTime? To)
{
    public const int DefaultPageSize = 10;

    /// <summary>
    /// The ceiling on <c>pageSize</c>. Without it the endpoint still hands over
    /// the whole table on request, which is the thing paging exists to stop —
    /// so the clamp, not the default, is what actually bounds the work.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Builds a query from raw query-string values. Named <c>Parse</c> rather
    /// than <c>From</c> because <see cref="From"/> is already the start of the
    /// due-date window.
    /// </summary>
    public static BillQuery Parse(
        int? page,
        int? pageSize,
        string? search,
        string? status,
        string? sort,
        string? dir,
        DateTime? from = null,
        DateTime? to = null) =>
        new(
            Page: Math.Max(1, page ?? 1),
            PageSize: Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize),
            Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Status: ParseStatus(status),
            Sort: ParseSort(sort),
            Descending: ParseDescending(dir),
            From: from,
            To: to);

    public static BillStatus ParseStatus(string? value) =>
        Enum.TryParse<BillStatus>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BillStatus.All;

    public static BillSort ParseSort(string? value) =>
        Enum.TryParse<BillSort>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BillSort.Id;

    /// <summary>Anything that is not recognisably "descending" sorts ascending,
    /// which is the direction a column header shows on its first click.</summary>
    public static bool ParseDescending(string? value) =>
        string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "descending", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Renders this back into a query string. Lives next to the parser so the
    /// two cannot drift: whatever the client writes here is what the endpoint
    /// reads above.
    /// </summary>
    public string ToQueryString()
    {
        var parts = new List<string>
        {
            $"page={Page}",
            $"pageSize={PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add($"search={Uri.EscapeDataString(Search)}");
        }

        if (Status != BillStatus.All)
        {
            parts.Add($"status={Status.ToString().ToLowerInvariant()}");
        }

        if (Sort != BillSort.Id || Descending)
        {
            parts.Add($"sort={char.ToLowerInvariant(Sort.ToString()[0])}{Sort.ToString()[1..]}");
            parts.Add($"dir={(Descending ? "desc" : "asc")}");
        }

        // Round-tripped as whole dates: the window is inclusive on both ends and
        // a time component would make "to" mean midnight, silently dropping the
        // last day. The endpoint compares on the date alone.
        if (From is { } from)
        {
            parts.Add($"from={from:yyyy-MM-dd}");
        }

        if (To is { } to)
        {
            parts.Add($"to={to:yyyy-MM-dd}");
        }

        return string.Join("&", parts);
    }
}
