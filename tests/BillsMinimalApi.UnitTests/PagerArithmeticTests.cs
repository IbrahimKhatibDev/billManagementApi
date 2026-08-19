using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The derived properties on <see cref="PagedResult{T}"/> — the ones a pager
/// renders itself from a page and a count.
/// <para>
/// None of them cross the wire: they are computed getters, so the integration
/// tests see <c>Page</c>, <c>PageSize</c> and <c>TotalCount</c> come back and
/// never the four figures the UI actually puts on screen. This is where those
/// four are checked.
/// </para>
/// </summary>
public sealed class PagerArithmeticTests
{
    private static PagedResult<string> Page(int page, int pageSize, int totalCount) =>
        new() { Page = page, PageSize = pageSize, TotalCount = totalCount };

    [Theory]
    [InlineData(0, 10, 1)]    // Empty, and still page 1 of 1.
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]   // Exactly full: one page, not two.
    [InlineData(11, 10, 2)]   // One over: the remainder gets a page of its own.
    [InlineData(25, 10, 3)]
    [InlineData(100, 10, 10)]
    public void The_page_count_rounds_up_and_never_reaches_zero(int totalCount, int pageSize, int expected)
    {
        // Never zero because "page 1 of 0" reads as a bug to anyone looking at
        // it, and an empty result is not a bug.
        Assert.Equal(expected, Page(1, pageSize, totalCount).TotalPages);
    }

    [Fact]
    public void A_page_size_of_nothing_is_answered_rather_than_divided_by()
    {
        // BillQuery clamps the page size before it gets here, so this is the
        // guard for a PagedResult built anywhere else — including the default
        // instance, where PageSize is 0 and a division would throw while a
        // component was rendering.
        Assert.Equal(1, Page(1, 0, 50).TotalPages);
        Assert.Equal(1, new PagedResult<string>().TotalPages);
    }

    [Fact]
    public void The_row_numbers_describe_the_slice_that_is_on_screen()
    {
        var page = Page(3, 10, 25);

        Assert.Equal(21, page.FirstRowNumber);

        // 30 would be the page's last slot; 25 is the last row that exists. The
        // caption reads "21 to 25 of 25", not "21 to 30 of 25".
        Assert.Equal(25, page.LastRowNumber);
    }

    [Fact]
    public void An_empty_result_counts_from_nothing_rather_than_from_one()
    {
        var page = PagedResult<string>.Empty(page: 1, pageSize: 10);

        Assert.Equal(0, page.FirstRowNumber);
        Assert.Equal(0, page.LastRowNumber);
        Assert.Equal(1, page.TotalPages);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);

        // The factory keeps what was asked for: the pager still has a page size
        // to show in its control after a search that matched nothing.
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(1, true, false)]    // First of three: nowhere back, somewhere on.
    [InlineData(2, true, true)]
    [InlineData(3, false, true)]    // Last of three.
    public void The_arrows_switch_off_at_the_ends(int page, bool hasNext, bool hasPrevious)
    {
        var result = Page(page, 10, 25);

        Assert.Equal(hasNext, result.HasNext);
        Assert.Equal(hasPrevious, result.HasPrevious);
    }

    [Fact]
    public void One_page_of_results_has_nowhere_to_go_in_either_direction()
    {
        var page = Page(1, 10, 4);

        Assert.False(page.HasNext);
        Assert.False(page.HasPrevious);
    }

    [Fact]
    public void A_page_past_the_end_is_the_one_slice_the_row_numbers_cannot_describe()
    {
        // BillQuery clamps the page number up to 1 but not down to the last page,
        // so ?page=9 on a three-page result is reachable by typing it. What comes
        // back is an empty page reporting rows 81 to 25 — nonsense, but harmless
        // nonsense: HasNext is false, so the pager offers the way back and no
        // link in either client can produce this in the first place.
        var page = Page(9, 10, 25);

        Assert.Equal(81, page.FirstRowNumber);
        Assert.Equal(25, page.LastRowNumber);
        Assert.False(page.HasNext);
        Assert.True(page.HasPrevious);
    }
}
