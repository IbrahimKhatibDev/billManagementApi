using System.Globalization;
using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
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

    public partial class Reports : IDisposable
    {
        /// <summary>Rows shown before the payee table asks to be expanded. Ten
        /// is enough to answer "who do I owe the most to" without turning the
        /// page into one long table.</summary>
        private const int PayeePreviewRows = 10;

        /// <summary>How many bills the "What to pay next" list shows. It is a
        /// shortlist, not a second bills page.</summary>
        private const int PriorityRows = 6;

        private const int DueSoonDays = 30;

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        private List<Bill> _bills = new();
        private bool _isLoading = true;
        private bool _loadFailed;

        private ReportRange _range = ReportRange.AllTime;

        /// <summary>The bills the whole page is computed from: everything inside
        /// the selected window. Cached rather than recomputed per property so a
        /// render does not re-filter the list a dozen times.</summary>
        private List<Bill> _scoped = new();

        private List<AgingBucket> _aging = new();
        private List<PayeeRow> _payees = new();
        private List<MonthRow> _months = new();
        private List<SizeBand> _bands = new();

        private PayeeSortColumn _payeeSort = PayeeSortColumn.Outstanding;
        private bool _payeeSortDescending = true;
        private bool _showAllPayees;

        /// <summary>Today, read once per load. Every "days late" figure on the
        /// page comes from this, so a circuit that stays open across midnight
        /// still renders a self-consistent report.</summary>
        private DateTime _today = DateTime.Today;

        private sealed record AgingBucket(
            string Label,
            int Count,
            decimal Amount,
            double BarPercent,
            string BarClass);

        private sealed record PayeeRow(
            string Payee,
            int Bills,
            decimal Billed,
            decimal Paid,
            decimal Outstanding);

        private sealed record MonthRow(
            string Label,
            int Bills,
            decimal Billed,
            decimal Paid,
            decimal Outstanding,
            double PaidPercent);

        private sealed record SizeBand(
            string Label,
            int Count,
            decimal Total,
            double BarPercent);

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

        private async Task LoadBillsAsync()
        {
            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                _bills = await BillService.GetBillsAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _bills = new List<Bill>();
                _loadFailed = true;
                Toasts.ShowError("Could not load reports. Is the API running?");
            }
            finally
            {
                _today = DateTime.Today;
                Rescope();
                _isLoading = false;
                StateHasChanged();
            }
        }

        private void SetRange(ReportRange range)
        {
            if (_range == range)
            {
                return;
            }

            _range = range;

            // A narrower window can leave fewer payees than the preview shows,
            // in which case an expanded table has nothing extra to reveal and
            // the collapse control disappears with it still expanded.
            _showAllPayees = false;

            Rescope();
        }

        /// <summary>Recomputes every section from the current window. One place,
        /// so the sections can never disagree about which bills they cover.
        /// </summary>
        private void Rescope()
        {
            _scoped = ReportRanges.Filter(_bills, _range, _today);

            BuildAging();
            BuildPayees();
            BuildMonths();
            BuildBands();
        }

        // -- Range framing --------------------------------------------------

        private string RangeCaption => _range.Caption(_today);

        /// <summary>Bills with no due date sit outside any bounded window. Saying
        /// so is the difference between "there are none" and "they are not being
        /// counted".</summary>
        private int UndatedExcluded => _range == ReportRange.AllTime
            ? 0
            : _bills.Count(b => b.DueDate is null);

        private string CsvHref => $"reports/bills.csv?range={_range.Slug()}";

        // -- Headline figures -----------------------------------------------

        private int BillCount => _scoped.Count;

        private decimal TotalBilled => _scoped.Sum(b => b.PaymentDue);

        private decimal PaidAmount => _scoped.Where(b => b.Paid).Sum(b => b.PaymentDue);

        private decimal OutstandingAmount => TotalBilled - PaidAmount;

        private int UnpaidCount => _scoped.Count(b => !b.Paid);

        /// <summary>
        /// Share of money settled, not of bills — ten small paid bills and one
        /// large unpaid one is not a 91% healthy picture.
        /// </summary>
        private double PaidPercent => TotalBilled == 0
            ? 0
            : (double)(PaidAmount / TotalBilled) * 100;

        private IEnumerable<Bill> OverdueBills => _scoped.Where(IsOverdue);

        private int OverdueCount => OverdueBills.Count();

        private decimal OverdueAmount => OverdueBills.Sum(b => b.PaymentDue);

        private Bill? LargestBill => _scoped
            .OrderByDescending(b => b.PaymentDue)
            .FirstOrDefault();

        private decimal AverageBill => BillCount == 0 ? 0 : TotalBilled / BillCount;

        private decimal MedianBill
        {
            get
            {
                if (BillCount == 0)
                {
                    return 0;
                }

                var sorted = _scoped.Select(b => b.PaymentDue).OrderBy(a => a).ToList();
                var middle = sorted.Count / 2;

                return sorted.Count % 2 == 1
                    ? sorted[middle]
                    : (sorted[middle - 1] + sorted[middle]) / 2;
            }
        }

        private IEnumerable<Bill> DueSoonBills => _scoped.Where(b =>
            !b.Paid
            && b.DueDate is { } due
            && due.Date >= _today
            && due.Date <= _today.AddDays(DueSoonDays));

        private int DueSoonCount => DueSoonBills.Count();

        private decimal DueSoonAmount => DueSoonBills.Sum(b => b.PaymentDue);

        // -- Overdue aging ---------------------------------------------------

        private void BuildAging()
        {
            var unpaid = _scoped.Where(b => !b.Paid).ToList();

            // Fixed buckets, built whether or not anything lands in them: an
            // empty "90+ days" row is information, and a table that grows and
            // shrinks as you change the range is hard to read across presets.
            var definitions = new (string Label, Func<int, bool> Matches, string BarClass)[]
            {
                ("Not yet due", days => days <= 0, "aging-bar-0"),
                ("1–30 days late", days => days is >= 1 and <= 30, "aging-bar-1"),
                ("31–60 days late", days => days is >= 31 and <= 60, "aging-bar-2"),
                ("61–90 days late", days => days is >= 61 and <= 90, "aging-bar-3"),
                ("Over 90 days late", days => days > 90, "aging-bar-4"),
            };

            var grouped = definitions
                .Select(d =>
                {
                    var matching = unpaid.Where(b => d.Matches(DaysLate(b))).ToList();
                    return (d.Label, d.BarClass, Count: matching.Count, Amount: matching.Sum(b => b.PaymentDue));
                })
                .ToList();

            // Bars are scaled against the biggest bucket, not against the total:
            // against the total, a healthy spread renders as five slivers.
            var max = grouped.Count == 0 ? 0m : grouped.Max(g => g.Amount);

            _aging = grouped
                .Select(g => new AgingBucket(
                    g.Label,
                    g.Count,
                    g.Amount,
                    max == 0 ? 0 : (double)(g.Amount / max) * 100,
                    g.BarClass))
                .ToList();
        }

        /// <summary>Days past due, or 0 for anything not yet due. Undated bills
        /// count as not yet due — they cannot be late without a date to be late
        /// against.</summary>
        private int DaysLate(Bill bill) => bill.DueDate is { } due
            ? Math.Max(0, (_today - due.Date).Days)
            : 0;

        private bool IsOverdue(Bill bill) =>
            !bill.Paid && bill.DueDate is { } due && due.Date < _today;

        /// <summary>
        /// The unpaid bills to deal with first: latest first, then whatever is
        /// due soonest. Undated bills sort last — they are never urgent.
        /// </summary>
        private IEnumerable<Bill> PriorityBills => _scoped
            .Where(b => !b.Paid)
            .OrderByDescending(DaysLate)
            .ThenBy(b => b.DueDate ?? DateTime.MaxValue)
            .Take(PriorityRows);

        private string PriorityNote(Bill bill)
        {
            var days = DaysLate(bill);

            if (days > 0)
            {
                return days == 1 ? "1 day late" : $"{days} days late";
            }

            if (bill.DueDate is not { } due)
            {
                return "no due date";
            }

            var until = (due.Date - _today).Days;

            return until switch
            {
                0 => "due today",
                1 => "due tomorrow",
                _ => $"in {until} days",
            };
        }

        // -- Payee breakdown -------------------------------------------------

        private void BuildPayees()
        {
            _payees = _scoped
                .GroupBy(b => b.PayeeName?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PayeeRow(
                    // The group key is whichever casing came first; take the
                    // first row's name so the table shows a real payee name.
                    Payee: string.IsNullOrEmpty(g.First().PayeeName) ? "(no name)" : g.First().PayeeName,
                    Bills: g.Count(),
                    Billed: g.Sum(b => b.PaymentDue),
                    Paid: g.Where(b => b.Paid).Sum(b => b.PaymentDue),
                    Outstanding: g.Where(b => !b.Paid).Sum(b => b.PaymentDue)))
                .ToList();
        }

        private IEnumerable<PayeeRow> SortedPayees
        {
            get
            {
                // Payee sorts alphabetically ascending by default; every money
                // column sorts biggest-first, because that is the question the
                // column exists to answer.
                IOrderedEnumerable<PayeeRow> ordered = _payeeSort switch
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

        private IOrderedEnumerable<PayeeRow> OrderPayees<TKey>(Func<PayeeRow, TKey> key) =>
            _payeeSortDescending ? _payees.OrderByDescending(key) : _payees.OrderBy(key);

        private IEnumerable<PayeeRow> VisiblePayees => _showAllPayees
            ? SortedPayees
            : SortedPayees.Take(PayeePreviewRows);

        private int HiddenPayeeCount => Math.Max(0, _payees.Count - PayeePreviewRows);

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

        private void BuildMonths()
        {
            _months = _scoped
                .Where(b => b.DueDate is not null)
                .GroupBy(b => new DateTime(b.DueDate!.Value.Year, b.DueDate.Value.Month, 1))
                // Newest first: the months you are being asked about now are the
                // recent ones, and they should not be at the bottom of a scroll.
                .OrderByDescending(g => g.Key)
                .Select(g =>
                {
                    var billed = g.Sum(b => b.PaymentDue);
                    var paid = g.Where(b => b.Paid).Sum(b => b.PaymentDue);

                    return new MonthRow(
                        Label: g.Key.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                        Bills: g.Count(),
                        Billed: billed,
                        Paid: paid,
                        Outstanding: billed - paid,
                        PaidPercent: billed == 0 ? 0 : (double)(paid / billed) * 100);
                })
                .ToList();
        }

        private int UndatedInScope => _scoped.Count(b => b.DueDate is null);

        // -- Bill size distribution ------------------------------------------

        private void BuildBands()
        {
            var definitions = new (string Label, Func<decimal, bool> Matches)[]
            {
                ("Under $50", amount => amount < 50),
                ("$50 – $99", amount => amount is >= 50 and < 100),
                ("$100 – $249", amount => amount is >= 100 and < 250),
                ("$250 – $499", amount => amount is >= 250 and < 500),
                ("$500 and over", amount => amount >= 500),
            };

            var grouped = definitions
                .Select(d =>
                {
                    var matching = _scoped.Where(b => d.Matches(b.PaymentDue)).ToList();
                    return (d.Label, Count: matching.Count, Total: matching.Sum(b => b.PaymentDue));
                })
                .ToList();

            // Scaled on count, not amount: this chart is about how many bills
            // are of each size, and scaling by money would make one large bill
            // outweigh twenty small ones.
            var max = grouped.Count == 0 ? 0 : grouped.Max(g => g.Count);

            _bands = grouped
                .Select(g => new SizeBand(
                    g.Label,
                    g.Count,
                    g.Total,
                    max == 0 ? 0 : g.Count * 100d / max))
                .ToList();
        }

        // -- Formatting ------------------------------------------------------

        /// <summary>Bar widths are written straight into a style attribute, so
        /// they are formatted invariantly — a comma decimal separator would be
        /// an invalid CSS length.</summary>
        private static string Width(double percent) =>
            percent.ToString("0.##", CultureInfo.InvariantCulture);

        private static string DueDateText(Bill bill) =>
            bill.DueDate?.ToString("MMM d, yyyy") ?? "—";
    }
}
