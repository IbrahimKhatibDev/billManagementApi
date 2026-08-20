using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The Overview. One <see cref="BillSummary"/> for the whole book, rendered
    /// as four sections: what is owed, how late it is, when it falls, and what
    /// needs settling today.
    /// <para>
    /// All the chart geometry that used to live here has moved into
    /// <see cref="TimelineLayout"/> and <see cref="StackedStrip"/>, where it can
    /// be tested. What is left is loading, and one write.
    /// </para>
    /// </summary>
    public partial class Index : IDisposable
    {
        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Stands in until the first response lands, so the markup can
        /// read a summary without a null check per section.</summary>
        private static readonly BillSummary NoData = new();

        private BillSummary? _summary;

        private bool _isLoading = true;
        private bool _loadFailed;

        /// <summary>The date the figures were computed against — the server's,
        /// from <see cref="BillSummary.AsOf"/>, so the timeline's "now" marker
        /// agrees with the bars beside it.
        /// <para>
        /// The seed is UTC because that is what the server's answer will be:
        /// the API reckons today as <c>UtcDateTime.Today</c>, and
        /// <see cref="DateTime.Today"/> is this host's local date, which west of
        /// Greenwich is yesterday's between local evening and midnight.
        /// </para>
        /// </summary>
        private DateTime _today = DateTime.UtcNow.Date;

        /// <summary>Which late bill is being settled, if any.</summary>
        private long? _busyId;

        /// <summary>
        /// Which load is the current one. Three callers put this page's load in
        /// flight — the first render, the <see cref="BillEventService"/> handler,
        /// and the resync after a refused write — and nothing serialises them, so
        /// they do not come back in the order they were sent. Without this a slow
        /// earlier response lands after a fast later one and puts a just-settled
        /// bill back in the late list. <c>Bills.razor.cs</c> guards its load the
        /// same way and for the same reason.
        /// </summary>
        private int _loadGeneration;

        private BillSummary Summary => _summary ?? NoData;

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += RefreshDashboard;
            await LoadStatsAsync();
        }

        public void Dispose() => BillEventService.OnBillsChanged -= RefreshDashboard;

        private void RefreshDashboard()
        {
            // Not `async void`: that form throws on a pooled thread with nobody
            // to catch it if the API is down. InvokeAsync also puts the work back
            // on the circuit's synchronization context, which StateHasChanged
            // requires.
            _ = InvokeAsync(LoadStatsAsync);
        }

        private async Task LoadStatsAsync()
        {
            var generation = ++_loadGeneration;

            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                // No window: the Overview is about everything on record, so it
                // asks for the unbounded summary.
                var summary = await BillService.GetSummaryAsync(from: null, to: null);

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

                // Back to the seed: NoData.AsOf is default(DateTime), and a
                // "now" marker in year 1 is not a marker.
                _today = DateTime.UtcNow.Date;
                _loadFailed = true;
                Toasts.ShowError("Could not load the overview. Is the API running?");
            }
            finally
            {
                // A superseded load must not clear the spinner: the load that
                // replaced it is still running.
                if (generation == _loadGeneration)
                {
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        private async Task MarkPaidAsync(SummaryBill bill)
        {
            if (_busyId is not null)
            {
                return;
            }

            _busyId = bill.Id;
            StateHasChanged();

            try
            {
                var result = await BillService.MarkPaidAsync(bill.Id);

                if (result.Success)
                {
                    Toasts.ShowSuccess($"{bill.PayeeName} marked paid.");

                    // Every other open page recomputes too — and this page is
                    // subscribed, so this is also what reloads it.
                    BillEventService.NotifyBillsChanged();
                    return;
                }

                Toasts.ShowError(result.ToMessage("update"));

                // A 409 or a 404 both mean this page is looking at stale data, so
                // the honest response is to go and get the current answer.
                await LoadStatsAsync();
            }
            finally
            {
                _busyId = null;
            }
        }
    }
}
