using BillsMinimalApi.Contracts;
using BillsMinimalApi.Data;
using BillsMinimalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Queries;

/// <summary>
/// Builds the whole Reports page in Postgres.
/// <para>
/// The page used to fetch every bill and aggregate the lot in the browser
/// circuit, which worked only because the table is small. Everything here is an
/// aggregate query instead: five round trips that each return a handful of rows,
/// rather than one round trip that returns the table.
/// </para>
/// <para>
/// The figures are deliberately identical to the ones the page computed in
/// memory, down to the bucket boundaries and the rounding — a report that
/// changed its answers when the arithmetic moved would be indistinguishable from
/// a report that was wrong before, or is wrong now.
/// </para>
/// </summary>
public static class BillSummaryBuilder
{
    private static readonly string[] AgingLabels =
    {
        "Not yet due",
        "1–30 days late",
        "31–60 days late",
        "61–90 days late",
        "Over 90 days late",
    };

    private static readonly string[] SizeBandLabels =
    {
        "Under $50",
        "$50 – $99",
        "$100 – $249",
        "$250 – $499",
        "$500 and over",
    };

    public static async Task<BillSummary> BuildAsync(
        AppDbContext db,
        DateTime? from,
        DateTime? to,
        DateTime today,
        CancellationToken cancellationToken)
    {
        // One window, applied once, reused by every section below. This is the
        // reason the whole page is a single endpoint: the sections cannot
        // disagree about which bills they cover if they are all filtered here.
        var scoped = db.Bills
            .AsNoTracking()
            .ApplyWindow(from, to);

        var summary = new BillSummary
        {
            AsOf = today,
            From = from?.Date,
            To = to?.Date,
        };

        await AddHeadlineAsync(scoped, summary, today, cancellationToken);
        await AddMedianAsync(scoped, summary, cancellationToken);
        summary.LargestBill = await LargestAsync(scoped, today, cancellationToken);
        summary.Aging = await AgingAsync(scoped, today, cancellationToken);
        summary.SizeBands = await SizeBandsAsync(scoped, cancellationToken);
        summary.Payees = await PayeesAsync(scoped, cancellationToken);
        summary.Months = await MonthsAsync(scoped, cancellationToken);
        summary.Weeks = await WeeksAsync(scoped, cancellationToken);
        summary.Priority = await PriorityAsync(scoped, today, cancellationToken);

        return summary;
    }

    /// <summary>
    /// Every headline figure in one statement, as conditional sums.
    /// <para>
    /// <c>Sum(b =&gt; condition ? 1 : 0)</c> rather than <c>Count(condition)</c>
    /// throughout: both translate, but the sum form makes it obvious that these
    /// are eight aggregates over one scan and not eight separate counts.
    /// </para>
    /// </summary>
    private static async Task AddHeadlineAsync(
        IQueryable<Bill> scoped,
        BillSummary summary,
        DateTime today,
        CancellationToken cancellationToken)
    {
        // "Due soon" is inclusive of the last day, and due dates carry a time of
        // day, so the bound is the midnight after it — the same reasoning as
        // ApplyWindow's upper bound.
        var dueSoonEnd = today.AddDays(BillSummary.DueSoonDays + 1);

        var totals = await scoped
            .GroupBy(_ => 1)
            .Select(g => new
            {
                BillCount = g.Count(),
                TotalBilled = g.Sum(b => b.PaymentDue),
                PaidAmount = g.Sum(b => b.Paid ? b.PaymentDue : 0m),
                UnpaidCount = g.Sum(b => b.Paid ? 0 : 1),
                OverdueCount = g.Sum(b => !b.Paid && b.DueDate < today ? 1 : 0),
                OverdueAmount = g.Sum(b => !b.Paid && b.DueDate < today ? b.PaymentDue : 0m),
                DueSoonCount = g.Sum(b =>
                    !b.Paid && b.DueDate >= today && b.DueDate < dueSoonEnd ? 1 : 0),
                DueSoonAmount = g.Sum(b =>
                    !b.Paid && b.DueDate >= today && b.DueDate < dueSoonEnd ? b.PaymentDue : 0m),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // No rows in scope: GROUP BY produces no groups, so this is null rather
        // than a row of zeroes. The summary's own defaults are already zero.
        if (totals is null)
        {
            return;
        }

        summary.BillCount = totals.BillCount;
        summary.TotalBilled = totals.TotalBilled;
        summary.PaidAmount = totals.PaidAmount;
        summary.UnpaidCount = totals.UnpaidCount;
        summary.OverdueCount = totals.OverdueCount;
        summary.OverdueAmount = totals.OverdueAmount;
        summary.DueSoonCount = totals.DueSoonCount;
        summary.DueSoonAmount = totals.DueSoonAmount;

        // Divided here rather than taken from SQL's AVG: AVG returns a numeric
        // whose scale Postgres picks, and the page has always shown
        // total ÷ count at the decimal's own precision.
        summary.AverageBill = totals.BillCount == 0
            ? 0
            : totals.TotalBilled / totals.BillCount;
    }

    /// <summary>
    /// The median amount, as one <c>OFFSET</c>/<c>LIMIT</c> over the sorted
    /// column.
    /// <para>
    /// Not <c>percentile_cont</c>, which is what you would write by hand: EF
    /// cannot translate it, and dropping to raw SQL for one figure would put the
    /// window filter in two languages. The skip lands on the middle element for
    /// an odd count and on the pair straddling the middle for an even one, so
    /// averaging what comes back is the median in both cases.
    /// </para>
    /// </summary>
    private static async Task AddMedianAsync(
        IQueryable<Bill> scoped,
        BillSummary summary,
        CancellationToken cancellationToken)
    {
        if (summary.BillCount == 0)
        {
            return;
        }

        var middle = await scoped
            .OrderBy(b => b.PaymentDue)
            .Select(b => b.PaymentDue)
            .Skip((summary.BillCount - 1) / 2)
            .Take(2 - (summary.BillCount % 2))
            .ToListAsync(cancellationToken);

        if (middle.Count > 0)
        {
            summary.MedianBill = middle.Sum() / middle.Count;
        }
    }

    /// <summary>The biggest bill in the window. The id tiebreak only decides
    /// which of two equal amounts is named, but without it the same request can
    /// name a different one each time.</summary>
    private static async Task<SummaryBill?> LargestAsync(
        IQueryable<Bill> scoped,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var largest = await scoped
            .OrderByDescending(b => b.PaymentDue)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return largest is null ? null : ToSummaryBill(largest, today);
    }

    /// <summary>
    /// The five aging buckets, over unpaid bills only.
    /// <para>
    /// Bucketed by comparing the due date against four precomputed dates rather
    /// than by subtracting dates and bucketing the difference. Postgres has no
    /// equivalent of <c>(today - due).Days</c> that EF will translate, and even
    /// if it did, an expression wrapped around the column could not use an
    /// index; four constants compared against a bare column can.
    /// </para>
    /// <para>
    /// The boundaries are the same ones the page used: a bill due exactly 30
    /// days ago is 30 days late and belongs in the second bucket, so that
    /// bucket's lower bound is inclusive and its upper bound is not.
    /// </para>
    /// </summary>
    private static async Task<List<AgingBucket>> AgingAsync(
        IQueryable<Bill> scoped,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var day30 = today.AddDays(-30);
        var day60 = today.AddDays(-60);
        var day90 = today.AddDays(-90);

        var grouped = await scoped
            .Where(b => !b.Paid)
            .GroupBy(b =>
                b.DueDate >= today ? 0
                : b.DueDate >= day30 ? 1
                : b.DueDate >= day60 ? 2
                : b.DueDate >= day90 ? 3
                : 4)
            .Select(g => new
            {
                Bucket = g.Key,
                Count = g.Count(),
                Amount = g.Sum(b => b.PaymentDue),
            })
            .ToListAsync(cancellationToken);

        // Rebuilt from the labels rather than from the rows: an empty bucket
        // produces no group, and a table whose rows appear and vanish as the
        // range changes is hard to read across presets.
        return AgingLabels
            .Select((label, index) =>
            {
                var row = grouped.FirstOrDefault(g => g.Bucket == index);

                return new AgingBucket
                {
                    Label = label,
                    Count = row?.Count ?? 0,
                    Amount = row?.Amount ?? 0m,
                };
            })
            .ToList();
    }

    /// <summary>The five size bands, over every bill in the window — paid ones
    /// included, because this describes what the bills look like, not what is
    /// owed.</summary>
    private static async Task<List<SizeBand>> SizeBandsAsync(
        IQueryable<Bill> scoped,
        CancellationToken cancellationToken)
    {
        var grouped = await scoped
            .GroupBy(b =>
                b.PaymentDue < 50m ? 0
                : b.PaymentDue < 100m ? 1
                : b.PaymentDue < 250m ? 2
                : b.PaymentDue < 500m ? 3
                : 4)
            .Select(g => new
            {
                Band = g.Key,
                Count = g.Count(),
                Total = g.Sum(b => b.PaymentDue),
            })
            .ToListAsync(cancellationToken);

        return SizeBandLabels
            .Select((label, index) =>
            {
                var row = grouped.FirstOrDefault(g => g.Band == index);

                return new SizeBand
                {
                    Label = label,
                    Count = row?.Count ?? 0,
                    Total = row?.Total ?? 0m,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Totals per payee.
    /// <para>
    /// Grouped on the trimmed, lower-cased name so that "Acme" and "ACME " are
    /// one payee, which is what the in-memory version's
    /// <c>StringComparer.OrdinalIgnoreCase</c> did. The display name is the
    /// minimum of the group rather than an arbitrary member, so the same data
    /// always names the payee the same way.
    /// </para>
    /// <para>
    /// Unpaged: the payee table is sorted by whichever column the user clicks,
    /// which needs the whole set. That is bounded by the number of distinct
    /// payees rather than by the number of bills, so it does not grow the way
    /// the bills list does — but it is the one part of this response that would
    /// need paging first if it ever did.
    /// </para>
    /// </summary>
    private static async Task<List<PayeeTotals>> PayeesAsync(
        IQueryable<Bill> scoped,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .GroupBy(b => b.PayeeName.Trim().ToLower())
            .Select(g => new
            {
                Payee = g.Min(b => b.PayeeName),
                Bills = g.Count(),
                Billed = g.Sum(b => b.PaymentDue),
                Paid = g.Sum(b => b.Paid ? b.PaymentDue : 0m),
            })
            .ToListAsync(cancellationToken);

        return rows
            // Biggest debt first, which is the question the table exists to
            // answer. The client re-sorts on click; this is only the default.
            .OrderByDescending(r => r.Billed - r.Paid)
            .ThenBy(r => r.Payee, StringComparer.OrdinalIgnoreCase)
            .Select(r => new PayeeTotals
            {
                Payee = string.IsNullOrWhiteSpace(r.Payee) ? "(no name)" : r.Payee,
                Bills = r.Bills,
                Billed = r.Billed,
                Paid = r.Paid,
            })
            .ToList();
    }

    /// <summary>Totals per month of due date, newest first.</summary>
    private static async Task<List<MonthTotals>> MonthsAsync(
        IQueryable<Bill> scoped,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .GroupBy(b => new { b.DueDate.Year, b.DueDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Bills = g.Count(),
                Billed = g.Sum(b => b.PaymentDue),
                Paid = g.Sum(b => b.Paid ? b.PaymentDue : 0m),
            })
            .OrderByDescending(g => g.Year)
            .ThenByDescending(g => g.Month)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new MonthTotals
            {
                Year = r.Year,
                Month = r.Month,
                Bills = r.Bills,
                Billed = r.Billed,
                Paid = r.Paid,
            })
            .ToList();
    }

    /// <summary>
    /// One row per week, paid and unpaid apart.
    /// <para>
    /// Grouped by due date in Postgres and folded into weeks in memory.
    /// <c>date_trunc('week', …)</c> has no dependable Npgsql translation, and
    /// grouping on the raw column needs none: due dates are stored at midnight
    /// UTC, so one group is one day. The fold that follows is pure arithmetic
    /// with a unit test of its own.
    /// </para>
    /// </summary>
    private static async Task<List<WeekTotals>> WeeksAsync(
        IQueryable<Bill> scoped,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .GroupBy(b => b.DueDate)
            .Select(g => new
            {
                Day = g.Key,
                Bills = g.Count(),
                Paid = g.Sum(b => b.Paid ? b.PaymentDue : 0m),
                Unpaid = g.Sum(b => b.Paid ? 0m : b.PaymentDue),
            })
            .ToListAsync(cancellationToken);

        return WeekBuckets.FromDays(
            rows.Select(r => new WeekBuckets.DayTotals(r.Day, r.Bills, r.Paid, r.Unpaid)),
            BillSummary.MaxWeeks);
    }

    /// <summary>
    /// The unpaid bills to deal with first.
    /// <para>
    /// The page expressed this as "most days late first, then soonest due",
    /// which collapses to a single ordering: everything already late, oldest
    /// first, then everything not yet due, soonest first. Both halves are due
    /// date ascending, so the only thing the sort needs is a flag putting the
    /// late ones ahead — and that is one comparison Postgres can do, where
    /// "days late, descending" is not.
    /// </para>
    /// </summary>
    private static async Task<List<SummaryBill>> PriorityAsync(
        IQueryable<Bill> scoped,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .Where(b => !b.Paid)
            .OrderBy(b => b.DueDate >= today ? 1 : 0)
            .ThenBy(b => b.DueDate)
            .ThenBy(b => b.Id)
            .Take(BillSummary.PriorityCount)
            .ToListAsync(cancellationToken);

        return rows.Select(b => ToSummaryBill(b, today)).ToList();
    }

    private static SummaryBill ToSummaryBill(Bill bill, DateTime today) => new()
    {
        Id = bill.Id,
        PayeeName = bill.PayeeName,
        DueDate = bill.DueDate,
        PaymentDue = bill.PaymentDue,

        // Computed here, in memory, against the same `today` the rest of the
        // response used — which is the whole reason the client is sent a number
        // instead of being left to subtract dates itself.
        DaysLate = Math.Max(0, (today - bill.DueDate.Date).Days),
    };
}
