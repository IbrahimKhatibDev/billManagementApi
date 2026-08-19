using System.Linq.Expressions;
using BillsMinimalApi.Contracts;
using BillsMinimalApi.Data;
using BillsMinimalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Queries;

/// <summary>
/// The filtering and ordering behind <c>GET /restapi/BillDtos</c>, written as
/// composable <see cref="IQueryable{T}"/> steps.
/// <para>
/// Every method here returns a query rather than results, which is the whole
/// point: the endpoint stacks them, then EF turns the stack into one SQL
/// statement with a WHERE, an ORDER BY and a LIMIT. Anything that took
/// <c>IEnumerable</c> instead would silently pull the table into memory first
/// and undo the change.
/// </para>
/// <para>
/// The summary endpoint applies the same <see cref="ApplyWindow"/> so a report
/// and a list asked about the same dates cover the same bills.
/// </para>
/// </summary>
public static class BillQueryable
{
    public static IQueryable<Bill> ApplyFilters(
        this IQueryable<Bill> bills,
        BillQuery query,
        DateTime today) =>
        bills
            .ApplyStatus(query.Status, today)
            .ApplyWindow(query.From, query.To)
            .ApplySearch(query.Search);

    /// <summary>
    /// "Overdue" is unpaid *and* past its due date, matching what the bills
    /// table has always shaded red. <paramref name="today"/> is passed in rather
    /// than read here so that one request cannot use two different ideas of
    /// today — a real risk for the summary endpoint, which asks this question
    /// several times per response.
    /// </summary>
    public static IQueryable<Bill> ApplyStatus(
        this IQueryable<Bill> bills,
        BillStatus status,
        DateTime today) => status switch
    {
        BillStatus.Paid => bills.Where(b => b.Paid),
        BillStatus.Unpaid => bills.Where(b => !b.Paid),
        BillStatus.Overdue => bills.Where(b => !b.Paid && b.DueDate < today),
        _ => bills,
    };

    /// <summary>
    /// The due-date window, inclusive at both ends.
    /// <para>
    /// The upper bound is expressed as <c>&lt; to + 1 day</c> rather than
    /// <c>&lt;= to</c> because due dates are stored as timestamps: a bill due on
    /// the last day of the window at any time other than midnight would fail
    /// <c>&lt;= to</c> and drop out. Both comparisons stay plain column-against-
    /// constant so an index on the column can still be used — wrapping the
    /// column in <c>date_trunc</c> or <c>CAST(... AS date)</c> would read the
    /// same and scan the table.
    /// </para>
    /// </summary>
    public static IQueryable<Bill> ApplyWindow(
        this IQueryable<Bill> bills,
        DateTime? from,
        DateTime? to)
    {
        if (from is { } start)
        {
            var lower = UtcDateTime.Normalize(start).Date;
            bills = bills.Where(b => b.DueDate >= lower);
        }

        if (to is { } end)
        {
            var upper = UtcDateTime.Normalize(end).Date.AddDays(1);
            bills = bills.Where(b => b.DueDate < upper);
        }

        return bills;
    }

    /// <summary>
    /// Matches the payee or the id, mirroring the substring search the bills
    /// page used to do in memory: typing "2" finds bill 2 and bill 12, and
    /// typing part of a payee name finds it regardless of case.
    /// <para>
    /// <c>ILIKE</c> rather than <c>lower(x) LIKE lower(y)</c> because Npgsql
    /// translates it directly, and the id side goes through a text cast so that
    /// a partial id still matches. Neither can use an index, which is fine at
    /// this size and is a deliberate trade: a trigram index would be the fix if
    /// the table ever justified one.
    /// </para>
    /// </summary>
    public static IQueryable<Bill> ApplySearch(this IQueryable<Bill> bills, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return bills;
        }

        var pattern = $"%{EscapeLike(search.Trim())}%";

        // The three-argument overloads matter: the two-argument ones emit
        // `ESCAPE ''`, which tells Postgres there is no escape character at all,
        // and the backslashes added by EscapeLike would then be matched as
        // literal backslashes while the wildcards they were meant to neutralise
        // went on wildcarding.
        return bills.Where(b =>
            EF.Functions.ILike(b.PayeeName, pattern, LikeEscape)
            || EF.Functions.Like(b.Id.ToString(), pattern, LikeEscape));
    }

    /// <summary>
    /// Orders the query, always with <see cref="Bill.Id"/> as a tiebreak.
    /// <para>
    /// The tiebreak is not cosmetic. Postgres gives no ordering guarantee
    /// between rows that tie on the sort key, so without it "sort by paid"
    /// across a 10-row page and its successor could show the same bill twice and
    /// never show another — the classic unstable-paging bug, and one that only
    /// appears on the columns with the fewest distinct values.
    /// </para>
    /// </summary>
    public static IOrderedQueryable<Bill> ApplySort(this IQueryable<Bill> bills, BillQuery query) =>
        query.Sort switch
        {
            BillSort.Payee => bills.By(b => b.PayeeName, query.Descending).ThenBy(b => b.Id),
            BillSort.DueDate => bills.By(b => b.DueDate, query.Descending).ThenBy(b => b.Id),
            BillSort.Amount => bills.By(b => b.PaymentDue, query.Descending).ThenBy(b => b.Id),
            BillSort.Paid => bills.By(b => b.Paid, query.Descending).ThenBy(b => b.Id),
            _ => bills.By(b => b.Id, query.Descending),
        };

    private static IOrderedQueryable<Bill> By<TKey>(
        this IQueryable<Bill> bills,
        Expression<Func<Bill, TKey>> key,
        bool descending) =>
        descending ? bills.OrderByDescending(key) : bills.OrderBy(key);

    /// <summary>A single backslash, passed to LIKE/ILIKE as the escape
    /// character.</summary>
    private const string LikeEscape = "\\";

    /// <summary>
    /// Neutralises the LIKE wildcards so a search for "50%" looks for those
    /// three characters rather than for anything starting with 50, and "_"
    /// matches an underscore rather than any single character. Backslashes are
    /// doubled first, or they would escape the escapes added after them.
    /// </summary>
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
