using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The bills table. Every question it asks — which rows, in what order, which
    /// page — is answered by Postgres rather than by LINQ over a full copy of the
    /// table, so the filter buttons, the column headers, the pager and the search
    /// box are all round trips.
    /// <para>
    /// <see cref="BillStatus"/> and <see cref="BillSort"/> come from the shared
    /// contracts project rather than being declared here. They used to be a local
    /// pair of enums with the same members, which was fine while the filtering
    /// happened in this file and became a translation layer the moment it did
    /// not.
    /// </para>
    /// </summary>
    public partial class Bills : IDisposable
    {
        private static readonly int[] PageSizeOptions = { 10, 25, 50 };

        /// <summary>
        /// How long the search box waits after the last keystroke. Long enough
        /// that typing a payee name is one query rather than eleven, short enough
        /// that it still feels like it is keeping up.
        /// </summary>
        private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Set by the dashboard's "Add a Bill" tile, which links to
        /// <c>bills?new=true</c>.</summary>
        [Parameter]
        [SupplyParameterFromQuery(Name = "new")]
        public bool OpenCreateForm { get; set; }

        private PagedResult<Bill> _result = PagedResult<Bill>.Empty(1, BillQuery.DefaultPageSize);
        private bool _isLoading = true;
        private bool _loadFailed;

        private string _searchText = string.Empty;
        private BillStatus _filter = BillStatus.All;

        /// <summary>Bills mid-flight in <see cref="TogglePaidAsync"/>. Keyed by
        /// id rather than a single bool so one slow row cannot freeze the whole
        /// table.</summary>
        private readonly HashSet<long> _togglingIds = new();

        private BillSort _sortColumn = BillSort.Id;
        private bool _sortDescending;

        private int _page = 1;
        private int _pageSize = BillQuery.DefaultPageSize;

        /// <summary>
        /// Overdue bills across the whole table, not just this page — the badge
        /// on the filter button is a reason to click it, so counting only the
        /// rows already on screen would defeat the point. Filled from a separate
        /// count request rather than from the rows.
        /// </summary>
        private int _overdueCount;

        /// <summary>
        /// Which load is the current one. A debounced keystroke, a filter click
        /// and a background refresh from <see cref="BillEventService"/> are
        /// routinely in flight together, and they do not come back in the order
        /// they were sent; without this, a slow early response can land after a
        /// fast later one and put the table back to what you already stopped
        /// asking for.
        /// </summary>
        private int _loadGeneration;

        /// <summary>Cancels the pending debounce when another key is pressed.
        /// Disposed in <see cref="Dispose"/> along with the event
        /// subscription.</summary>
        private CancellationTokenSource? _searchCts;

        // Modal state. The three "modals" are plain conditional rendering with
        // Bootstrap's classes — no bootstrap.bundle.js, no JS interop, which
        // matters because IJSRuntime is unusable during the prerender pass.
        private enum FormMode
        {
            None,
            Create,
            Edit,
        }

        private FormMode _formMode = FormMode.None;
        private Bill _formBill = new();
        private Bill? _deleteTarget;
        private bool _isSaving;

        // -- What the markup binds ---------------------------------------------

        private IReadOnlyList<Bill> PagedBills => _result.Items;

        private bool HasRows => _result.Items.Count > 0;

        /// <summary>Total matching the current filter and search, which is what
        /// the pager and the "showing 1–10 of 34" line describe.</summary>
        private int FilteredCount => _result.TotalCount;

        /// <summary>
        /// Distinguishes "you have no bills" from "nothing matches what you
        /// asked for" without a second request: a zero count while nothing is
        /// filtered or searched can only be the former.
        /// </summary>
        private bool HasNoBillsAtAll =>
            _result.TotalCount == 0 && _filter == BillStatus.All && !HasSearch;

        private int TotalPages => _result.TotalPages;

        private int CurrentPage => _result.Page;

        private int FirstRowNumber => _result.FirstRowNumber;

        private int LastRowNumber => _result.LastRowNumber;

        private int PageSize => _pageSize;

        private int OverdueCount => _overdueCount;

        private bool HasSearch => !string.IsNullOrEmpty(_searchText);

        /// <summary>A window of at most five page numbers centred on the current
        /// page, so 50 pages do not render 50 buttons.</summary>
        private IEnumerable<int> PageWindow
        {
            get
            {
                const int Window = 5;

                var start = Math.Max(1, CurrentPage - (Window / 2));
                var end = Math.Min(TotalPages, start + Window - 1);
                start = Math.Max(1, end - Window + 1);

                return Enumerable.Range(start, end - start + 1);
            }
        }

        // -- Controls -----------------------------------------------------------

        /// <summary>
        /// Every control resets to page 1 before reloading. Narrowing a 25-row
        /// result to 4 matches while standing on page 3 would otherwise ask the
        /// server for a page that no longer exists — it clamps rather than
        /// erroring, but landing on the bottom of results whose top you never saw
        /// is its own kind of wrong.
        /// </summary>
        private Task SetFilterAsync(BillStatus filter)
        {
            if (_filter == filter)
            {
                return Task.CompletedTask;
            }

            _filter = filter;
            _page = 1;

            return LoadBillsAsync();
        }

        /// <summary>
        /// Bound to the rows-per-page select. A plain <c>@bind</c> would need the
        /// value round-tripped through a string anyway, and the page has to be
        /// reset and the rows refetched either way.
        /// </summary>
        private Task OnPageSizeChangedAsync(ChangeEventArgs e)
        {
            if (!int.TryParse(e.Value?.ToString(), out var size) || size == _pageSize)
            {
                return Task.CompletedTask;
            }

            _pageSize = size;
            _page = 1;

            return LoadBillsAsync();
        }

        private Task GoToPageAsync(int page)
        {
            var next = Math.Clamp(page, 1, TotalPages);

            if (next == _page)
            {
                return Task.CompletedTask;
            }

            _page = next;

            return LoadBillsAsync();
        }

        private Task SortByAsync(BillSort column)
        {
            if (_sortColumn == column)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortColumn = column;
                _sortDescending = false;
            }

            // Re-sorting reorders the entire result set, so page 3 now holds
            // different bills than the ones being read a moment ago. Going back
            // to the top is the only reading of "sort by amount" that makes
            // sense.
            _page = 1;

            return LoadBillsAsync();
        }

        /// <summary>
        /// Used by the mobile sort control, where the header row that normally
        /// carries <see cref="SortByAsync"/> is hidden. Picking a column from a
        /// dropdown must not flip the direction the way clicking a header does
        /// — there is a separate button for that.
        /// </summary>
        private Task SetSortColumnAsync(ChangeEventArgs e)
        {
            if (!Enum.TryParse<BillSort>(e.Value?.ToString(), out var column)
                || column == _sortColumn)
            {
                return Task.CompletedTask;
            }

            _sortColumn = column;
            _page = 1;

            return LoadBillsAsync();
        }

        private Task ToggleSortDirectionAsync()
        {
            _sortDescending = !_sortDescending;
            _page = 1;

            return LoadBillsAsync();
        }

        /// <summary>Marks the header the table is currently ordered by, so the
        /// caret on that one column renders solid instead of ghosted.</summary>
        private string? Sorted(BillSort column) =>
            _sortColumn == column ? "sorted" : null;

        private string SortCaret(BillSort column)
        {
            if (_sortColumn != column)
            {
                return "ph-arrows-down-up";
            }

            return _sortDescending ? "ph-caret-down" : "ph-caret-up";
        }

        /// <summary>
        /// Waits out <see cref="SearchDebounce"/> before asking the server, and
        /// abandons the wait if another key arrives. Bound to <c>oninput</c>
        /// rather than through <c>@bind</c>: two-way binding would fire a request
        /// per keystroke, which is exactly what the debounce exists to prevent.
        /// </summary>
        private async Task OnSearchInputAsync(ChangeEventArgs e)
        {
            var next = e.Value?.ToString() ?? string.Empty;

            if (_searchText == next)
            {
                return;
            }

            _searchText = next;
            _page = 1;

            // Cancel first, then dispose: Cancel runs the delay's registration
            // synchronously, so by the time it returns there is nothing left
            // holding the source.
            _searchCts?.Cancel();
            _searchCts?.Dispose();

            var cts = new CancellationTokenSource();
            _searchCts = cts;

            try
            {
                await Task.Delay(SearchDebounce, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Another key arrived; that keystroke owns the request now.
                return;
            }

            await LoadBillsAsync();
        }

        private Task ClearSearchAsync()
        {
            if (!HasSearch)
            {
                return Task.CompletedTask;
            }

            // No debounce on the way back to empty: pressing Clear is a decision,
            // not a keystroke on the way to one.
            _searchCts?.Cancel();
            _searchText = string.Empty;
            _page = 1;

            return LoadBillsAsync();
        }

        // -- Loading ------------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            // The page only published this event before, so a bill created on
            // the dashboard left this table stale until a manual reload.
            BillEventService.OnBillsChanged += OnBillsChanged;

            // Here rather than in OnParametersSet: this runs exactly once per
            // component instance, so the form cannot pop open again on some
            // later re-render while the user is part-way through dismissing it.
            if (OpenCreateForm)
            {
                OpenCreateModal();
            }

            await LoadBillsAsync();
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= OnBillsChanged;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
        }

        private void OnBillsChanged()
        {
            // Never `async void`: the handler is invoked from whatever thread
            // raised the event, and an unhandled exception there has no
            // SynchronizationContext to marshal it back to the circuit.
            _ = InvokeAsync(LoadBillsAsync);
        }

        /// <summary>
        /// Turns the current state of the page into one request. Deliberately the
        /// only place that happens, so the pager, the header carets and the
        /// search box cannot end up describing different queries.
        /// </summary>
        private BillQuery BuildQuery() => new(
            Page: _page,
            PageSize: _pageSize,
            Search: _searchText,
            Status: _filter,
            Sort: _sortColumn,
            Descending: _sortDescending,
            From: null,
            To: null);

        /// <summary>
        /// Kept parameterless so the markup can still write
        /// <c>@onclick="LoadBillsAsync"</c> — an optional parameter would break
        /// the method-group conversion the event binding relies on.
        /// </summary>
        private async Task LoadBillsAsync()
        {
            var generation = ++_loadGeneration;

            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                // Concurrently: the badge count asks a different question from
                // the rows (every overdue bill, regardless of what is filtered or
                // searched), so it cannot be derived from them — but it need not
                // wait for them either.
                var rows = BillService.GetBillsAsync(BuildQuery());
                var overdue = BillService.CountAsync(BillStatus.Overdue);

                await Task.WhenAll(rows, overdue);

                if (generation != _loadGeneration)
                {
                    return;
                }

                _result = rows.Result;
                _overdueCount = overdue.Result;

                // The server clamps a page past the end and tells us where we
                // actually landed. Taking its answer is what keeps the pager
                // honest after a delete empties the last page.
                _page = _result.Page;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // In Blazor Server an unhandled exception kills the circuit and
                // replaces the page with the yellow error bar — very likely on
                // first load if the blazor container outruns the api container.
                if (generation != _loadGeneration)
                {
                    return;
                }

                _result = PagedResult<Bill>.Empty(_page, _pageSize);
                _overdueCount = 0;
                _loadFailed = true;
                Toasts.ShowError("Could not load bills. Is the API running?");
            }
            finally
            {
                // A superseded load must not clear the spinner: the load that
                // replaced it is still running, and the table would flash from
                // dimmed to crisp and back.
                if (generation == _loadGeneration)
                {
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        // -- Presentation -------------------------------------------------------

        /// <summary>
        /// Past its due date and still unpaid. Compared against
        /// <see cref="DateTime.Today"/>, not <c>UtcNow</c>: the API stores due
        /// dates as midnight UTC, so anything time-of-day aware would call a
        /// bill due today overdue for part of the day in a western timezone.
        /// </summary>
        private static bool IsOverdue(Bill bill) =>
            !bill.Paid && bill.DueDate is { } due && due.Date < DateTime.Today;

        /// <summary>Three states from two booleans: an unpaid bill that is not
        /// due yet is not a problem, so it stays grey and the red is saved for
        /// the ones that are actually late.</summary>
        private static string StatusClass(Bill bill) => bill switch
        {
            { Paid: true } => "bg-success",
            _ when IsOverdue(bill) => "bg-danger",
            _ => "bg-secondary",
        };

        private static string StatusText(Bill bill) => bill switch
        {
            { Paid: true } => "Paid",
            _ when IsOverdue(bill) => "Overdue",
            _ => "Unpaid",
        };

        /// <summary>The date itself. Spelled out rather than "6/2/2026", which
        /// reads as either 6 February or June 2 depending on where you are
        /// from.</summary>
        private static string DueDateText(Bill bill) =>
            bill.DueDate?.ToString("MMM d, yyyy") ?? "—";

        /// <summary>
        /// How far off the due date is, in plain words. Only rendered for
        /// unpaid bills — once something is paid, how late it was is history.
        /// </summary>
        private static string? DueRelativeText(Bill bill)
        {
            if (bill.Paid || bill.DueDate is not { } due)
            {
                return null;
            }

            var days = (due.Date - DateTime.Today).Days;

            return days switch
            {
                0 => "due today",
                1 => "due tomorrow",
                < 0 and > -2 => "1 day late",
                < 0 => $"{-days} days late",
                <= 7 => $"in {days} days",
                _ => null,
            };
        }

        // -- Writes -------------------------------------------------------------

        private void OpenCreateModal()
        {
            _formBill = new Bill { DueDate = DateTime.Today };
            _formMode = FormMode.Create;
        }

        private void OpenEditModal(Bill bill)
        {
            // A copy, not the table's instance: cancelling the form must not
            // leave half-typed values rendered in the row behind it.
            _formBill = new Bill
            {
                Id = bill.Id,
                PayeeName = bill.PayeeName,
                DueDate = bill.DueDate,
                PaymentDue = bill.PaymentDue,
                Paid = bill.Paid,
                Version = bill.Version,
            };

            _formMode = FormMode.Edit;
        }

        private void OpenDeleteModal(Bill bill) => _deleteTarget = bill;

        private void CloseForm()
        {
            _formMode = FormMode.None;
            _isSaving = false;
        }

        private void CloseDelete()
        {
            _deleteTarget = null;
            _isSaving = false;
        }

        private bool IsToggling(Bill bill) => _togglingIds.Contains(bill.Id);

        /// <summary>
        /// Marks a bill paid or unpaid straight from the table. Ticking a
        /// checkbox is the most common thing anyone does on this page, and
        /// routing it through the edit modal meant opening a form, changing one
        /// checkbox, and submitting five fields back.
        /// </summary>
        private async Task TogglePaidAsync(Bill bill)
        {
            // Add returns false if the id is already in the set, which is what
            // stops a double-click sending two writes.
            if (!_togglingIds.Add(bill.Id))
            {
                return;
            }

            // Optimistic: flip now and put it back if the write fails. This
            // table is read far more often than it is written, and waiting a
            // round trip to tick a box makes the whole page feel remote.
            bill.Paid = !bill.Paid;

            try
            {
                var result = await BillService.UpdateBillAsync(bill);

                if (!result.Success)
                {
                    bill.Paid = !bill.Paid;
                    Toasts.ShowError(result.ToMessage("update"));

                    // Our copy is stale (409) or the row is gone (404); either
                    // way what is on screen is wrong, so resync.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(bill.Paid ? "Marked as paid" : "Marked as unpaid");

                // Not merely to refresh the dashboard: the API increments
                // Version on every write, so the copy in this list is now stale
                // and a second toggle would 409 against it.
                AfterWrite();
            }
            finally
            {
                _togglingIds.Remove(bill.Id);
            }
        }

        private async Task SaveFormAsync()
        {
            if (_isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var creating = _formMode == FormMode.Create;

                var result = creating
                    ? await BillService.CreateBillAsync(_formBill)
                    : await BillService.UpdateBillAsync(_formBill);

                if (!result.Success)
                {
                    Toasts.ShowError(result.ToMessage(creating ? "create" : "update"));

                    // A 409 means someone else won the race and our copy is
                    // stale, and a 404 means the row is gone — in both cases the
                    // list on screen is wrong, so refresh it before the retry.
                    // The form stays open with the user's values intact.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(creating ? "Bill created" : "Bill updated");
                CloseForm();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private async Task ConfirmDeleteAsync()
        {
            if (_deleteTarget is not { } bill || _isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var result = await BillService.DeleteBillAsync(bill.Id);

                if (!result.Success)
                {
                    Toasts.ShowError(result.ToMessage("delete"));

                    if (result.IsNotFound)
                    {
                        CloseDelete();
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess("Bill deleted");
                CloseDelete();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void AfterWrite()
        {
            // Publishing is enough: this page subscribes too, so the dashboard
            // recomputes its counters and charts and the table reloads from the
            // one notification — no double fetch. Scoped per circuit, so it
            // never reaches another connected browser.
            BillEventService.NotifyBillsChanged();
        }
    }
}
