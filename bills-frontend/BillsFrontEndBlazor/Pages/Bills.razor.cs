using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The bills page. It holds the whole book — filtered and searched by
    /// Postgres, then partitioned into due windows here — rather than one page at
    /// a time, because a due window spans the book and cannot be assembled from a
    /// slice of it.
    /// <para>
    /// The pager and the column sorts went with that change: the groups impose
    /// their own order, and a book sorted by amount has no due windows in it.
    /// </para>
    /// </summary>
    public partial class Bills : IDisposable
    {
        /// <summary>
        /// The most rows the page will hold at once.
        /// <para>
        /// Not a display limit — a real bound. Every row rendered into a Blazor
        /// Server circuit is state the server keeps and diffs on every change, so
        /// an unbounded book would make one large account slow for everybody
        /// sharing the host. When it bites, the page says so.
        /// </para>
        /// </summary>
        private const int RowCap = 500;

        /// <summary>
        /// How long the search box waits after the last keystroke. Long enough
        /// that typing a payee name is one query rather than eleven, short enough
        /// that it still feels like it is keeping up.
        /// </summary>
        private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

        /// <summary>The chips, in the order the design puts them.</summary>
        private static readonly BillStatus[] FilterOrder =
        {
            BillStatus.All,
            BillStatus.Unpaid,
            BillStatus.Overdue,
            BillStatus.Paid,
        };

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Set by the Overview's create link, which points at
        /// <c>bills?new=true</c>.</summary>
        [Parameter]
        [SupplyParameterFromQuery(Name = "new")]
        public bool OpenCreateForm { get; set; }

        /// <summary>
        /// Set by the Overview's "Clear the N late bills" link, which points at
        /// <c>bills?filter=overdue</c>. A string rather than a
        /// <see cref="BillStatus"/> so an unrecognised value falls back to All
        /// instead of failing to bind.
        /// </summary>
        [Parameter]
        [SupplyParameterFromQuery(Name = "filter")]
        public string? FilterName { get; set; }

        private BillBook _book = BillBook.Empty;
        private bool _isLoading = true;
        private bool _loadFailed;

        private string _searchText = string.Empty;
        private BillStatus _filter = BillStatus.All;

        /// <summary>
        /// Overdue bills across the whole table, not just what is loaded — the
        /// badge on the chip is a reason to click it, so counting only the rows on
        /// screen would defeat the point.
        /// </summary>
        private int _overdueCount;

        /// <summary>Every bill on the account, for the "N of M" tally.</summary>
        private int _billCount;

        /// <summary>
        /// The date the groups are cut against, fixed for the whole load so five
        /// sections cannot disagree about where the week ends. This is Blazor
        /// Server, so <see cref="DateTime.Today"/> is the API host's own date.
        /// </summary>
        private DateTime _today = DateTime.Today;

        /// <summary>Bills mid-flight in <see cref="TogglePaidAsync"/>. Keyed by id
        /// rather than one bool so a slow row cannot freeze the whole page.</summary>
        private readonly HashSet<long> _busyIds = new();

        /// <summary>
        /// Ids of the checked rows.
        /// <para>
        /// Ids rather than <see cref="Bill"/> instances: every load replaces the
        /// list with fresh objects carrying fresh concurrency tokens, and a set
        /// of stale references would quietly hold the old ones.
        /// </para>
        /// </summary>
        private readonly HashSet<long> _selectedIds = new();

        private bool _isBulkWriting;

        /// <summary>
        /// Which load is the current one. A debounced keystroke, a chip click and
        /// a background refresh from <see cref="BillEventService"/> are routinely
        /// in flight together and do not come back in the order they were sent;
        /// without this a slow early response can land after a fast later one and
        /// put the page back to what you already stopped asking for.
        /// </summary>
        private int _loadGeneration;

        /// <summary>Cancels the pending debounce when another key is pressed.</summary>
        private CancellationTokenSource? _searchCts;

        // Modal state. The two "modals" are plain conditional rendering with
        // Bootstrap's classes — no bootstrap.bundle.js, no JS interop, which
        // matters because IJSRuntime is unusable during the prerender pass.
        /// <summary>
        /// Whether the create modal is open. A bool now that the same block no
        /// longer has to be two forms — editing happens in the rows.
        /// </summary>
        private bool _isCreating;

        private Bill _formBill = new();
        private Bill? _deleteTarget;
        private bool _isSaving;

        // -- What the markup binds ---------------------------------------------

        /// <summary>One due-window section, ready to render.</summary>
        private sealed record BillSection(
            DueWindow Window,
            string Title,
            string Tone,
            List<Bill> Bills,
            decimal Total);

        private IReadOnlyList<Bill> Rows => _book.Bills;

        private bool HasRows => _book.Bills.Count > 0;

        /// <summary>How many match the chip and the search — which is not
        /// <c>Rows.Count</c> once the cap bites.</summary>
        private int MatchCount => _book.TotalCount;

        private int BillCount => _billCount;

        private int OverdueCount => _overdueCount;

        private bool HasSearch => !string.IsNullOrEmpty(_searchText);

        /// <summary>
        /// Distinguishes "you have no bills" from "nothing matches what you asked
        /// for" without a second request: a zero count while nothing is filtered
        /// or searched can only be the former.
        /// </summary>
        private bool HasNoBillsAtAll =>
            _book.TotalCount == 0 && _filter == BillStatus.All && !HasSearch;

        private decimal LoadedTotal => _book.Bills.Sum(b => b.PaymentDue);

        /// <summary>
        /// The five sections, empty ones dropped.
        /// <para>
        /// Computed per render rather than cached, so an optimistic paid toggle
        /// moves its row into the Paid group immediately instead of waiting for
        /// the reload. It is a partition of at most <see cref="RowCap"/> rows.
        /// </para>
        /// </summary>
        private IEnumerable<BillSection> Sections =>
            DueWindows.Order
                .Select(window => (
                    Window: window,
                    Bills: _book.Bills
                        .Where(b => DueWindows.Classify(b.Paid, b.DueDate, _today) == window)
                        // Soonest first within a section, which is the order the
                        // grouping exists to express. Id breaks ties so the list
                        // does not reshuffle between renders.
                        .OrderBy(b => b.DueDate ?? DateTime.MaxValue)
                        .ThenBy(b => b.Id)
                        .ToList()))
                .Where(g => g.Bills.Count > 0)
                .Select(g => new BillSection(
                    g.Window,
                    DueWindows.Title(g.Window),
                    Tone(g.Window),
                    g.Bills,
                    g.Bills.Sum(b => b.PaymentDue)));

        /// <summary>The dot colour per section. Tokens only — the component it is
        /// handed to never names a palette.</summary>
        private static string Tone(DueWindow window) => window switch
        {
            DueWindow.Late => "var(--late)",
            DueWindow.ThisWeek => "var(--accent)",
            DueWindow.ThisMonth => "var(--text)",
            DueWindow.Later => "var(--muted)",
            _ => "var(--ok)",
        };

        private int SelectedCount => _selectedIds.Count;

        /// <summary>
        /// The selected bills, resolved against what is loaded. Safe to sum
        /// because <see cref="LoadBillsAsync"/> prunes the selection to the rows
        /// on screen — an id in the set is always an id in the list.
        /// </summary>
        private IEnumerable<Bill> SelectedBills =>
            _book.Bills.Where(b => _selectedIds.Contains(b.Id));

        private decimal SelectedTotal => SelectedBills.Sum(b => b.PaymentDue);

        /// <summary>How many of the selection there is anything to do to.</summary>
        private int PayableCount => SelectedBills.Count(b => !b.Paid);

        private void ToggleSelected(Bill bill)
        {
            // Remove returns false when it was not there, which is the cheapest
            // correct way to write "toggle".
            if (!_selectedIds.Remove(bill.Id))
            {
                _selectedIds.Add(bill.Id);
            }
        }

        private void ClearSelection() => _selectedIds.Clear();

        /// <summary>
        /// Marks every unpaid bill in the selection as paid. The point of the
        /// whole idea: eight late bills in one gesture rather than eight clicks,
        /// eight round trips and eight re-renders.
        /// </summary>
        private async Task MarkSelectedPaidAsync()
        {
            if (_isBulkWriting)
            {
                return;
            }

            // Materialised before the awaits: SelectedBills is a live query over
            // state that the reload at the end of this method replaces.
            var payable = SelectedBills.Where(b => !b.Paid).ToList();

            if (payable.Count == 0)
            {
                return;
            }

            _isBulkWriting = true;

            try
            {
                var result = await BillService.MarkManyPaidAsync(payable);

                if (result.Success)
                {
                    Toasts.ShowSuccess(result.ToMessage());
                }
                else
                {
                    Toasts.ShowError(result.ToMessage());
                }

                // Cleared whatever happened. The successful writes are committed,
                // so leaving the batch selected invites a second run at bills that
                // are already paid — and the message has already said how many did
                // not land.
                _selectedIds.Clear();

                // Not merely to refresh the Overview: every bill written now has a
                // higher Version, so the copies in this list are stale.
                AfterWrite();
            }
            finally
            {
                _isBulkWriting = false;
            }
        }

        // -- Controls -----------------------------------------------------------

        private Task SetFilterAsync(BillStatus filter)
        {
            if (_filter == filter)
            {
                return Task.CompletedTask;
            }

            _filter = filter;

            return LoadBillsAsync();
        }

        /// <summary>
        /// Waits out <see cref="SearchDebounce"/> before asking the server, and
        /// abandons the wait if another key arrives.
        /// </summary>
        private async Task OnSearchInputAsync(ChangeEventArgs e)
        {
            var next = e.Value?.ToString() ?? string.Empty;

            if (_searchText == next)
            {
                return;
            }

            _searchText = next;

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

            return LoadBillsAsync();
        }

        // -- Loading ------------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += OnBillsChanged;

            // Here rather than in OnParametersSet: this runs exactly once per
            // component instance, so a deep link cannot re-apply its filter — or
            // pop the form open again — on some later re-render while the user is
            // part-way through changing it.
            ApplyQueryFilter();

            if (OpenCreateForm)
            {
                OpenCreateModal();
            }

            await LoadBillsAsync();
        }

        /// <summary>
        /// Honours <c>?filter=overdue</c>. Case-insensitive because the link is
        /// written in a URL, and validated because anyone can type anything into
        /// one — an unrecognised value leaves the chip on All rather than
        /// throwing.
        /// </summary>
        private void ApplyQueryFilter()
        {
            if (Enum.TryParse<BillStatus>(FilterName, ignoreCase: true, out var status)
                && Enum.IsDefined(status))
            {
                _filter = status;
            }
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
        /// only place that happens, so the chips, the search box and the tally
        /// cannot end up describing different queries.
        /// </summary>
        private BillQuery BuildQuery() => new(
            Page: 1,
            PageSize: BillQuery.MaxPageSize,
            Search: _searchText,
            Status: _filter,
            // The groups re-sort within themselves anyway; asking the server for
            // due-date order means the cap keeps the soonest bills rather than an
            // arbitrary 500.
            Sort: BillSort.DueDate,
            Descending: false,
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
            _today = DateTime.Today;
            StateHasChanged();

            try
            {
                // Concurrently: the two counts ask different questions from the
                // rows — every overdue bill and every bill, regardless of what is
                // filtered or searched — so neither can be derived from them, but
                // neither need wait for them either.
                var book = BillService.GetBookAsync(BuildQuery(), RowCap);
                var overdue = BillService.CountAsync(BillStatus.Overdue);
                var everything = BillService.CountAsync(BillStatus.All);

                await Task.WhenAll(book, overdue, everything);

                if (generation != _loadGeneration)
                {
                    return;
                }

                _book = book.Result;
                _overdueCount = overdue.Result;
                _billCount = everything.Result;

                // Drop anything that is no longer on screen — a different chip, a
                // narrower search, a deleted bill, or a row past the cap. The bar
                // reports a count and a total, and both would be lies if the set
                // could hold bills the page cannot show.
                //
                // Pruning rather than clearing outright, which is what the design
                // prototype did on every filter change: a selection that survives
                // narrowing the list is the more useful of the two, and pruning is
                // needed here anyway for deletes and the cap.
                _selectedIds.IntersectWith(_book.Bills.Select(b => b.Id));
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

                _book = BillBook.Empty;
                _overdueCount = 0;
                _billCount = 0;
                _selectedIds.Clear();
                _loadFailed = true;
                Toasts.ShowError("Could not load bills. Is the API running?");
            }
            finally
            {
                // A superseded load must not clear the spinner: the load that
                // replaced it is still running, and the page would flash from
                // dimmed to crisp and back.
                if (generation == _loadGeneration)
                {
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        // -- Writes -------------------------------------------------------------

        private void OpenCreateModal()
        {
            _formBill = new Bill { DueDate = DateTime.Today };
            _isCreating = true;
        }

        private void OpenDeleteModal(Bill bill) => _deleteTarget = bill;

        private void CloseForm()
        {
            _isCreating = false;
            _isSaving = false;
        }

        private void CloseDelete()
        {
            _deleteTarget = null;
            _isSaving = false;
        }

        /// <summary>
        /// Marks a bill paid or unpaid straight from its row. Ticking a box is the
        /// most common thing anyone does on this page, and routing it through the
        /// edit modal meant opening a form, changing one checkbox, and submitting
        /// five fields back.
        /// </summary>
        private async Task TogglePaidAsync(Bill bill)
        {
            // Add returns false if the id is already in the set, which is what
            // stops a double-click sending two writes.
            if (!_busyIds.Add(bill.Id))
            {
                return;
            }

            // Optimistic: flip now and put it back if the write fails. Sections is
            // computed per render, so the row moves to its new group immediately.
            bill.Paid = !bill.Paid;

            try
            {
                var result = await BillService.UpdateBillAsync(bill);

                if (!result.Success)
                {
                    bill.Paid = !bill.Paid;
                    Toasts.ShowError(result.ToMessage("update"));

                    // Our copy is stale (409) or the row is gone (404); either way
                    // what is on screen is wrong, so resync.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(bill.Paid ? "Marked as paid" : "Marked as unpaid");

                // Not merely to refresh the Overview: the API increments Version
                // on every write, so the copy in this list is now stale and a
                // second toggle would 409 against it.
                AfterWrite();
            }
            finally
            {
                _busyIds.Remove(bill.Id);
            }
        }

        /// <summary>
        /// Saves one inline edit. The same optimistic shape as
        /// <see cref="TogglePaidAsync"/>: apply it, write it, put it back if the
        /// server refuses.
        /// </summary>
        private async Task SaveEditAsync(BillEdit edit)
        {
            var bill = edit.Bill;

            // Guards a second edit landing on a bill that already has a write in
            // flight — the version in hand would be one behind by the time it
            // arrived, and the second write would 409 against our own first one.
            if (!_busyIds.Add(bill.Id))
            {
                return;
            }

            // The three editable fields, kept so a refusal can be undone. Cheaper
            // and more honest than reloading: a failed write should leave the page
            // exactly as it was, not as the server last saw it.
            var payee = bill.PayeeName;
            var dueDate = bill.DueDate;
            var amount = bill.PaymentDue;

            edit.Apply(bill);

            try
            {
                var result = await BillService.UpdateBillAsync(bill);

                if (!result.Success)
                {
                    bill.PayeeName = payee;
                    bill.DueDate = dueDate;
                    bill.PaymentDue = amount;

                    Toasts.ShowError(result.ToMessage("update"));

                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess("Bill updated");

                // A due-date edit can move the row to another group, and every
                // write bumps Version — so the list has to come back either way.
                AfterWrite();
            }
            finally
            {
                _busyIds.Remove(bill.Id);
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
                var result = await BillService.CreateBillAsync(_formBill);

                if (!result.Success)
                {
                    // The form stays open with the values intact, so a rejection
                    // costs a retry rather than the whole entry.
                    Toasts.ShowError(result.ToMessage("create"));
                    return;
                }

                Toasts.ShowSuccess("Bill created");
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
            // Publishing is enough: this page subscribes too, so the Overview
            // recomputes and this list reloads from the one notification — no
            // double fetch. Scoped per circuit, so it never reaches another
            // connected browser.
            BillEventService.NotifyBillsChanged();
        }
    }
}
