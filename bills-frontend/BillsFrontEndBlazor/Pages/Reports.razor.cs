using System.Globalization;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    public enum PayeeSortColumn
    {
        Payee,
        Bills,
        Billed,
        Paid,
        Outstanding,
    }

    /// <summary>
    /// The reports page. Every figure on it is computed by Postgres and arrives
    /// in one <see cref="BillSummary"/> response.
    /// <para>
    /// It used to fetch the whole table and aggregate in C#, which was fine
    /// until the list endpoint started paging — at which point "every bill" was
    /// quietly ten of them, and a report is the one page that cannot be allowed
    /// to describe a page of data as though it were the set. Asking the server
    /// for the aggregates is both the fix and the faster answer.
    /// </para>
    /// <para>
    /// What is left here is presentation: bar widths, sort order for the payee
    /// table, and how a month is spelled.
    /// </para>
    /// </summary>
    public partial class Reports : IDisposable
    {
        /// <summary>Rows shown before the payee table asks to be expanded. Ten
        /// is enough to answer "who do I owe the most to" without turning the
        /// page into one long table.</summary>
        private const int PayeePreviewRows = 10;

        /// <summary>Stands in until the first response lands, so every headline
        /// property can read from a summary without a null check apiece.
        /// </summary>
        private static readonly BillSummary NoData = new();

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        private BillSummary? _summary;
        private bool _isLoading = true;
        private bool _loadFailed;

        private ReportRange _range = ReportRange.AllTime;

        private List<AgingRow> _aging = new();
        private List<PayeeTotals> _payees = new();
        private List<MonthTotals> _months = new();
        private List<BandRow> _bands = new();

        private PayeeSortColumn _payeeSort = PayeeSortColumn.Outstanding;
        private bool _payeeSortDescending = true;
        private bool _showAllPayees;

        /// <summary>
        /// The date the figures on screen were computed against — the server's,
        /// taken from <see cref="BillSummary.AsOf"/>, not this machine's. They
        /// are usually the same day, and when they are not it is the response
        /// that decides what "3 days late" means, because the response is where
        /// the number came from.
        /// </summary>
        private DateTime _today = DateTime.Today;

        /// <summary>Bumped on every load so the headline counters replay from
        /// zero. Without it, switching to a range whose totals happen to match
        /// the previous one — or refreshing unchanged data — animates nothing.
        /// See <c>AnimatedCounter.Generation</c>.</summary>
        private int _animationGeneration;

        /// <summary>Which load is the current one; see the same field in
        /// Bills.razor.cs. Clicking through the range presets faster than the
        /// server answers is the case this exists for.</summary>
        private int _loadGeneration;

        /// <summary>An aging bucket with the two things only the client decides:
        /// how wide its bar is and what colour.</summary>
        private sealed record AgingRow(
            string Label,
            int Count,
            decimal Amount,
            double BarPercent,
            string BarClass);

        private sealed record BandRow(
            string Label,
            int Count,
            decimal Total,
            double BarPercent);

        private BillSummary Summary => _summary ?? NoData;

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += OnBillsChanged;
            await LoadBillsAsync();
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= OnBillsChanged;
        }

        private void OnBillsChanged()
        {
            // Never `async void` — see Bills.razor.cs. InvokeAsync also puts the
            // work back on the circuit's synchronization context.
            _ = InvokeAsync(LoadBillsAsync);
        }

        /// <summary>
        /// Kept parameterless so the markup can bind it directly to the Refresh
        /// and Retry buttons.
        /// </summary>
        private async Task LoadBillsAsync()
        {
            var generation = ++_loadGeneration;

            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                // The window is resolved against this machine's date to ask the
                // question; the answer comes back stamped with the date it was
                // actually computed against, and that is what gets rendered.
                var (from, to) = _range.Window(DateTime.Today);

                var summary = await BillService.GetSummaryAsync(from, to);

                if (generation != _loadGeneration)
                {
                    return;
                }

                _summary = summary;
                _today = summary.AsOf;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (generation != _loadGeneration)
                {
                    return;
                }

                _summary = NoData;
                _today = DateTime.Today;
                _loadFailed = true;
                Toasts.ShowError("Could not load reports. Is the API running?");
            }
            finally
            {
                if (generation == _loadGeneration)
                {
                    Rebuild();
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        private Task SetRangeAsync(ReportRange range)
        {
            if (_range == range)
            {
                return Task.CompletedTask;
            }

            _range = range;

            // A narrower window can leave fewer payees than the preview shows,
            // in which case an expanded table has nothing extra to reveal and
            // the collapse control disappears with it still expanded.
            _showAllPayees = false;

            return LoadBillsAsync();
        }

        /// <summary>
        /// Turns one response into the shapes the markup renders. One place, so
        /// the sections can never disagree about which bills they cover — and
        /// the one place that can never forget to replay the counters.
        /// </summary>
        private void Rebuild()
        {
            _animationGeneration++;

            _payees = Summary.Payees;
            _months = Summary.Months;

            BuildAging();
            BuildBands();
        }

        // -- Range framing --------------------------------------------------

        private string RangeCaption => _range.Caption(_today);

        private string CsvHref => $"reports/bills.csv?range={_range.Slug()}";

        /// <summary>
        /// "All time and nothing in it" is the only way to be sure there are no
        /// bills at all rather than none in this window — which is the
        /// difference between offering to create one and suggesting a wider
        /// range.
        /// </summary>
        private bool HasNoBillsAtAll => _range == ReportRange.AllTime && BillCount == 0;

        // -- Headline figures -----------------------------------------------

        private int BillCount => Summary.BillCount;

        private decimal TotalBilled => Summary.TotalBilled;

        private decimal PaidAmount => Summary.PaidAmount;

        private decimal OutstandingAmount => Summary.OutstandingAmount;

        private int UnpaidCount => Summary.UnpaidCount;

        private double PaidPercent => Summary.PaidPercent;

        private int OverdueCount => Summary.OverdueCount;

        private decimal OverdueAmount => Summary.OverdueAmount;

        private SummaryBill? LargestBill => Summary.LargestBill;

        private decimal AverageBill => Summary.AverageBill;

        private decimal MedianBill => Summary.MedianBill;

        private int DueSoonCount => Summary.DueSoonCount;

        private decimal DueSoonAmount => Summary.DueSoonAmount;

        // -- Overdue aging ---------------------------------------------------

        /// <summary>
        /// The buckets themselves come from the server, always five and always
        /// in order; what is added here is the bar. It is scaled against the
        /// biggest bucket rather than the total, because against the total a
        /// healthy spread renders as five slivers.
        /// </summary>
        private void BuildAging()
        {
            var buckets = Summary.Aging;
            var max = buckets.Count == 0 ? 0m : buckets.Max(b => b.Amount);

            _aging = buckets
                .Select((bucket, index) => new AgingRow(
                    bucket.Label,
                    bucket.Count,
                    bucket.Amount,
                    max == 0 ? 0 : (double)(bucket.Amount / max) * 100,

                    // Positional, so the ramp from grey through to red follows
                    // the order the server sends rather than a label match that
                    // would break the day a bucket is reworded.
                    $"aging-bar-{index}"))
                .ToList();
        }

        private IReadOnlyList<SummaryBill> PriorityBills => Summary.Priority;

        /// <summary>Days late is computed server-side against the same date as
        /// the rest of the response, so anything late is overdue by
        /// definition.</summary>
        private static bool IsOverdue(SummaryBill bill) => bill.DaysLate > 0;

        private string PriorityNote(SummaryBill bill)
        {
            if (bill.DaysLate > 0)
            {
                return bill.DaysLate == 1 ? "1 day late" : $"{bill.DaysLate} days late";
            }

            var until = (bill.DueDate.Date - _today.Date).Days;

            return until switch
            {
                0 => "due today",
                1 => "due tomorrow",
                _ => $"in {until} days",
            };
        }

        // -- Payee breakdown -------------------------------------------------

        private IEnumerable<PayeeTotals> SortedPayees
        {
            get
            {
                // Payee sorts alphabetically ascending by default; every money
                // column sorts biggest-first, because that is the question the
                // column exists to answer.
                IOrderedEnumerable<PayeeTotals> ordered = _payeeSort switch
                {
                    PayeeSortColumn.Payee => OrderPayees(p => p.Payee),
                    PayeeSortColumn.Bills => OrderPayees(p => p.Bills),
                    PayeeSortColumn.Billed => OrderPayees(p => p.Billed),
                    PayeeSortColumn.Paid => OrderPayees(p => p.Paid),
                    _ => OrderPayees(p => p.Outstanding),
                };

                return ordered.ThenBy(p => p.Payee);
            }
        }

        private IOrderedEnumerable<PayeeTotals> OrderPayees<TKey>(Func<PayeeTotals, TKey> key) =>
            _payeeSortDescending ? _payees.OrderByDescending(key) : _payees.OrderBy(key);

        private IEnumerable<PayeeTotals> VisiblePayees => _showAllPayees
            ? SortedPayees
            : SortedPayees.Take(PayeePreviewRows);

        private int HiddenPayeeCount => Math.Max(0, _payees.Count - PayeePreviewRows);

        /// <summary>
        /// Reorders in place rather than refetching. The server already sent
        /// every payee in the window — this is the one table on the page that is
        /// complete in the response, so sorting it is a render, not a request.
        /// </summary>
        private void SortPayeesBy(PayeeSortColumn column)
        {
            if (_payeeSort == column)
            {
                _payeeSortDescending = !_payeeSortDescending;
                return;
            }

            _payeeSort = column;

            // Names read best A–Z; amounts read best largest-first.
            _payeeSortDescending = column != PayeeSortColumn.Payee;
        }

        private string? PayeeSorted(PayeeSortColumn column) =>
            _payeeSort == column ? "sorted" : null;

        private string PayeeSortCaret(PayeeSortColumn column)
        {
            if (_payeeSort != column)
            {
                return "bi-arrow-down-up";
            }

            return _payeeSortDescending ? "bi-caret-down-fill" : "bi-caret-up-fill";
        }

        // -- Month by month --------------------------------------------------

        /// <summary>
        /// The server sends a year and a month, not "March 2026": how a month is
        /// spelled depends on the culture the page is rendered in, and that is
        /// not something a JSON response should be deciding.
        /// </summary>
        private static string MonthLabel(MonthTotals month) =>
            month.FirstDay.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        // -- Bill size distribution ------------------------------------------

        /// <summary>
        /// Bars scaled on count, not amount: this chart is about how many bills
        /// are of each size, and scaling by money would make one large bill
        /// outweigh twenty small ones.
        /// </summary>
        private void BuildBands()
        {
            var bands = Summary.SizeBands;
            var max = bands.Count == 0 ? 0 : bands.Max(b => b.Count);

            _bands = bands
                .Select(band => new BandRow(
                    band.Label,
                    band.Count,
                    band.Total,
                    max == 0 ? 0 : band.Count * 100d / max))
                .ToList();
        }

        // -- Formatting ------------------------------------------------------

        /// <summary>Bar widths are written straight into a style attribute, so
        /// they are formatted invariantly — a comma decimal separator would be
        /// an invalid CSS length.</summary>
        private static string Width(double percent) =>
            percent.ToString("0.##", CultureInfo.InvariantCulture);

        private static string DueDateText(SummaryBill bill) =>
            bill.DueDate.ToString("MMM d, yyyy");
    }
}
