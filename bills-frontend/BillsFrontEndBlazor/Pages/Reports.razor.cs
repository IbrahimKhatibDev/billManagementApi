using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
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
    /// What is left here is loading and framing. The two charts own their own
    /// arithmetic — <c>PaidRateStrip</c> and <c>PayeePareto</c> take the raw
    /// rows off the summary — and the one sentence this page still computes,
    /// it computes through <see cref="SizeBandSentence"/>, which is unit-tested
    /// away from the renderer.
    /// </para>
    /// </summary>
    public partial class Reports : IDisposable
    {
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

        private string? _bandSentence;

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

            return LoadBillsAsync();
        }

        /// <summary>
        /// The per-response work that is not a component's: replay the
        /// counters, and re-read the size-band sentence. One place, so the two
        /// can never be done for different responses.
        /// </summary>
        private void Rebuild()
        {
            _animationGeneration++;
            _bandSentence = SizeBandSentence.Describe(Summary.SizeBands);
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
    }
}
