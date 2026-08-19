namespace BillsMinimalApi.Contracts;

/// <summary>
/// One page of results plus the count the pager needs. <see cref="TotalCount"/>
/// is the count of everything matching the filters, not of
/// <see cref="Items"/> — without it a client cannot render "page 3 of 12", and
/// "is there a next page" degenerates into fetching one more row to find out.
/// </summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = new();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    /// <summary>
    /// Computed rather than stored, so it can never disagree with the two values
    /// it is derived from. At least 1: a client rendering "page 1 of 0" for an
    /// empty result reads as a bug.
    /// </summary>
    public int TotalPages => PageSize <= 0
        ? 1
        : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    /// <summary>1-based row number of the first item on this page, or 0 when the
    /// result set is empty.</summary>
    public int FirstRowNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastRowNumber => Math.Min(Page * PageSize, TotalCount);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = new List<T>(), Page = page, PageSize = pageSize, TotalCount = 0 };
}
