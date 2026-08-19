using System.Globalization;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// <see cref="BillQuery.ToQueryString"/> against <see cref="BillQuery.Parse"/>.
/// <para>
/// The two live in the same file so that they cannot drift, which is a claim
/// rather than a guarantee until something checks it. Nothing did: the
/// integration suite writes its query strings out literally on purpose — so that
/// it exercises the parsing a browser would reach, not the round trip of the type
/// that does the parsing — which leaves the writing half with no coverage at all.
/// The Blazor client uses that half for every link on the bills page.
/// </para>
/// </summary>
public sealed class QueryStringRoundTripTests
{
    public static TheoryData<BillQuery> Queries() => new()
    {
        BillQuery.Parse(null, null, null, null, null, null),
        BillQuery.Parse(3, 25, "acme", "unpaid", "dueDate", "desc"),
        BillQuery.Parse(1, 100, "  spaces  ", "overdue", "amount", "asc"),
        BillQuery.Parse(2, 10, null, "paid", "payee", "descending"),
        BillQuery.Parse(
            1, 10, null, null, null, null,
            new DateTime(2026, 1, 1), new DateTime(2026, 3, 31)),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void A_query_survives_being_written_out_and_read_back(BillQuery original)
    {
        var round = Reparse(original);

        // Record equality, so this compares all eight components at once and a
        // new one added to BillQuery is covered here the moment it exists —
        // which is the case this test is really guarding, since a field the
        // writer forgets is exactly how the two halves drift apart.
        Assert.Equal(original, round);
    }

    [Fact]
    public void A_search_term_survives_the_characters_that_would_end_a_query_string()
    {
        // & and = would otherwise split into parameters of their own, + would
        // arrive as a space, and % starts an escape sequence. All four are things
        // somebody can type into a search box, and the last one is a LIKE
        // wildcard as well — so it has two ways to go wrong on one trip.
        var original = BillQuery.Parse(1, 10, "a&b=c+d 50%", null, null, null);

        Assert.Equal("a&b=c+d 50%", Reparse(original).Search);
    }

    [Fact]
    public void The_default_query_says_only_what_it_has_to()
    {
        // Page and size always, because a pager needs them stated. Nothing else:
        // a link carrying status=all&sort=id&dir=asc says no more than a bare one
        // and reads like a filter is applied when none is.
        Assert.Equal("page=1&pageSize=10", BillQuery.Parse(null, null, null, null, null, null).ToQueryString());
    }

    [Fact]
    public void Sorting_descending_on_the_default_column_is_still_written_out()
    {
        // Sort is Id, which is the default, so a writer testing only the column
        // would drop the direction with it and quietly flip the page back to
        // ascending. The condition is `Sort != Id || Descending` for this row.
        var query = BillQuery.Parse(null, null, null, null, "id", "desc");

        Assert.Contains("dir=desc", query.ToQueryString());
        Assert.True(Reparse(query).Descending);
    }

    [Fact]
    public void The_sort_column_is_written_the_way_a_client_spells_it()
    {
        // camelCase, matching every other query parameter on the URL. Parse
        // accepts any casing, so this is about what the app puts in a browser's
        // address bar rather than about what it can read back.
        Assert.Contains("sort=dueDate", BillQuery.Parse(null, null, null, null, "DueDate", null).ToQueryString());
    }

    [Fact]
    public void The_window_is_written_as_whole_dates()
    {
        var query = BillQuery.Parse(
            null, null, null, null, null, null,
            new DateTime(2026, 3, 15, 13, 45, 0),
            new DateTime(2026, 3, 31, 23, 59, 59));

        // No time component on either end. "to" is the one that matters: a
        // timestamp there means midnight, which silently drops everything due on
        // the last day of the window the user asked for.
        Assert.Contains("from=2026-03-15", query.ToQueryString());
        Assert.Contains("to=2026-03-31", query.ToQueryString());
    }

    private static BillQuery Reparse(BillQuery query)
    {
        var values = query.ToQueryString()
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

        // Invariant throughout, because the writer is invariant: a machine on a
        // culture that reads "03/04" the other way round should still round-trip.
        return BillQuery.Parse(
            Value(values, "page") is { } page ? int.Parse(page, Invariant) : null,
            Value(values, "pageSize") is { } size ? int.Parse(size, Invariant) : null,
            Value(values, "search"),
            Value(values, "status"),
            Value(values, "sort"),
            Value(values, "dir"),
            Value(values, "from") is { } from ? DateTime.Parse(from, Invariant) : null,
            Value(values, "to") is { } to ? DateTime.Parse(to, Invariant) : null);
    }

    private static string? Value(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
}
