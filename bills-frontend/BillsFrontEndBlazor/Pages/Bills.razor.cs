using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// Overdue is deliberately a peer of Unpaid rather than a separate axis,
    /// even though it is a strict subset of it: the filter is one row of
    /// buttons answering one question — "which bills am I looking at?" — and
    /// two independent controls would let you ask for Paid + Overdue, which is
    /// always empty.
    /// </summary>
    public enum BillFilter
    {
        All,
        Paid,
        Unpaid,
        Overdue,
    }

    public enum BillSortColumn
    {
        Id,
        Payee,
        DueDate,
        Amount,
        Paid,
    }

    public partial class Bills : IDisposable
    {
        private static readonly int[] PageSizeOptions = { 10, 25, 50 };

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

        private List<Bill> _bills = new();
        private bool _isLoading = true;
        private bool _loadFailed;

        private string _searchText = string.Empty;
        private BillFilter _filter = BillFilter.All;

        /// <summary>Bills mid-flight in <see cref="TogglePaidAsync"/>. Keyed by
        /// id rather than a single bool so one slow row cannot freeze the whole
        /// table.</summary>
        private readonly HashSet<long> _togglingIds = new();

        private BillSortColumn _sortColumn = BillSortColumn.Id;
        private bool _sortDescending;

        private int _page = 1;
        private int _pageSize = 10;

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

        /// <summary>
        /// Bound by the search box. The pager reset is the point of the property:
        /// filtering from page 3 of 25 rows down to 4 matches would otherwise
        /// strand the user on an empty page, or — after clamping — on the bottom
        /// of results whose top they never saw.
        /// </summary>
        private string SearchText
        {
            get => _searchText;
            set
            {
                var next = value ?? string.Empty;

                if (_searchText == next)
                {
                    return;
                }

                _searchText = next;
                _page = 1;
            }
        }

        private bool HasSearch => !string.IsNullOrEmpty(_searchText);

        private void SetFilter(BillFilter filter)
        {
            if (_filter == filter)
            {
                return;
            }

            _filter = filter;
            _page = 1;
        }

        private int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pageSize == value)
                {
                    return;
                }

                _pageSize = value;
                _page = 1;
            }
        }

        /// <summary>
        /// Bound to the rows-per-page select. A plain <c>@bind</c> would need the
        /// value round-tripped through a string anyway, and going through
        /// <see cref="PageSize"/> is what resets the pager.
        /// </summary>
        private void OnPageSizeChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var size))
            {
                PageSize = size;
            }
        }

        private IEnumerable<Bill> FilteredBills
        {
            get
            {
                var filtered = _bills.Where(MatchesFilters);

                // DueDate is nullable, so sort the nulls to the end rather than
                // letting them lead every ascending sort.
                return _sortColumn switch
                {
                    BillSortColumn.Payee => Order(filtered, b => b.PayeeName),
                    BillSortColumn.DueDate => Order(filtered, b => b.DueDate ?? DateTime.MaxValue),
                    BillSortColumn.Amount => Order(filtered, b => b.PaymentDue),
                    BillSortColumn.Paid => Order(filtered, b => b.Paid),
                    _ => Order(filtered, b => b.Id),
                };
            }
        }

        private IOrderedEnumerable<Bill> Order<TKey>(IEnumerable<Bill> source, Func<Bill, TKey> key)
            => _sortDescending ? source.OrderByDescending(key) : source.OrderBy(key);

        private int FilteredCount => _bills.Count(MatchesFilters);

        private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)_pageSize));

        /// <summary>Current page, clamped — a delete can shrink the result set
        /// under the page the user is standing on.</summary>
        private int CurrentPage => Math.Clamp(_page, 1, TotalPages);

        private IEnumerable<Bill> PagedBills => FilteredBills
            .Skip((CurrentPage - 1) * _pageSize)
            .Take(_pageSize);

        private int FirstRowNumber => FilteredCount == 0 ? 0 : ((CurrentPage - 1) * _pageSize) + 1;

        private int LastRowNumber => Math.Min(CurrentPage * _pageSize, FilteredCount);

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

        private void GoToPage(int page) => _page = Math.Clamp(page, 1, TotalPages);

        private void SortBy(BillSortColumn column)
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
        }

        /// <summary>
        /// Used by the mobile sort control, where the header row that normally
        /// carries <see cref="SortBy"/> is hidden. Picking a column from a
        /// dropdown must not flip the direction the way clicking a header does
        /// — there is a separate button for that.
        /// </summary>
        private void SetSortColumn(ChangeEventArgs e)
        {
            if (Enum.TryParse<BillSortColumn>(e.Value?.ToString(), out var column))
            {
                _sortColumn = column;
            }
        }

        private void ToggleSortDirection() => _sortDescending = !_sortDescending;

        /// <summary>Marks the header the table is currently ordered by, so the
        /// caret on that one column renders solid instead of ghosted.</summary>
        private string? Sorted(BillSortColumn column) =>
            _sortColumn == column ? "sorted" : null;

        private string SortCaret(BillSortColumn column)
        {
            if (_sortColumn != column)
            {
                return "bi-arrow-down-up";
            }

            return _sortDescending ? "bi-caret-down-fill" : "bi-caret-up-fill";
        }

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
        }

        private void OnBillsChanged()
        {
            // Never `async void`: the handler is invoked from whatever thread
            // raised the event, and an unhandled exception there has no
            // SynchronizationContext to marshal it back to the circuit.
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
                // In Blazor Server an unhandled exception kills the circuit and
                // replaces the page with the yellow error bar — very likely on
                // first load if the blazor container outruns the api container.
                _bills = new List<Bill>();
                _loadFailed = true;
                Toasts.ShowError("Could not load bills. Is the API running?");
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private bool MatchesFilters(Bill bill)
        {
            var matchesStatus = _filter switch
            {
                BillFilter.Paid => bill.Paid,
                BillFilter.Unpaid => !bill.Paid,
                BillFilter.Overdue => IsOverdue(bill),
                _ => true,
            };

            if (!matchesStatus)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return true;
            }

            var term = _searchText.Trim();

            return bill.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
                   || bill.PayeeName.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        private void ClearSearch() => SearchText = string.Empty;

        /// <summary>
        /// Past its due date and still unpaid. Compared against
        /// <see cref="DateTime.Today"/>, not <c>UtcNow</c>: the API stores due
        /// dates as midnight UTC, so anything time-of-day aware would call a
        /// bill due today overdue for part of the day in a western timezone.
        /// </summary>
        private static bool IsOverdue(Bill bill) =>
            !bill.Paid && bill.DueDate is { } due && due.Date < DateTime.Today;

        private int OverdueCount => _bills.Count(IsOverdue);

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
