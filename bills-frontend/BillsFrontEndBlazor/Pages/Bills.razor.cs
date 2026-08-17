using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using BillsFrontEndBlazor.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace BillsFrontEndBlazor.Pages
{
    public enum PaidFilter
    {
        All,
        Paid,
        Unpaid,
    }

    public partial class Bills : IDisposable
    {
        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public IDialogService DialogService { get; set; } = default!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = default!;

        private MudTable<Bill> _table = default!;
        private List<Bill> _bills = new();
        private string _searchText = string.Empty;
        private PaidFilter _paidFilter = PaidFilter.All;
        private bool _isLoading = true;
        private bool _loadFailed;

        /// <summary>
        /// Bound by the toolbar's search box. The explicit
        /// <see cref="MudTableBase.NavigateTo(Page)"/> is the point of the
        /// property: MudTable does not rewind its pager when the filter shrinks
        /// the result set, it only clamps to the last valid page. Filtering from
        /// page 3 of 25 rows down to 13 matches lands the user on "11-13 of 13"
        /// — the bottom of results they have not seen the top of.
        /// </summary>
        private string SearchText
        {
            get => _searchText;
            set
            {
                // MudTextField's Clearable button hands back null, not "".
                var next = value ?? string.Empty;

                if (_searchText == next)
                {
                    return;
                }

                _searchText = next;
                _table?.NavigateTo(Page.First);
            }
        }

        /// <summary>
        /// Bound by the All/Paid/Unpaid toggle. Same pager reset as
        /// <see cref="SearchText"/>.
        /// </summary>
        private PaidFilter PaidFilterValue
        {
            get => _paidFilter;
            set
            {
                if (_paidFilter == value)
                {
                    return;
                }

                _paidFilter = value;
                _table?.NavigateTo(Page.First);
            }
        }

        // Dialogs are full width on phones, capped at Small on desktop.
        private static readonly DialogOptions FormOptions = new()
        {
            FullWidth = true,
            MaxWidth = MaxWidth.Small,
            CloseButton = true,
            BackdropClick = false,
        };

        protected override async Task OnInitializedAsync()
        {
            // The page only published this event before, so a bill created on
            // the dashboard left this table stale until a manual reload.
            BillEventService.OnBillsChanged += OnBillsChanged;
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
                Snackbar.Add("Could not load bills. Is the API running?", Severity.Error);
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private bool MatchesFilters(Bill bill)
        {
            var matchesStatus = _paidFilter switch
            {
                PaidFilter.Paid => bill.Paid,
                PaidFilter.Unpaid => !bill.Paid,
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

        private async Task OpenCreateDialogAsync()
        {
            var draft = new Bill
            {
                DueDate = DateTime.Today,
            };

            await ShowFormDialogAsync("New Bill", draft, "Create", SaveNewAsync);
        }

        private async Task OpenEditDialogAsync(Bill bill)
        {
            // A copy, not the table's instance: cancelling the dialog must not
            // leave half-typed values rendered in the row behind it.
            var draft = new Bill
            {
                Id = bill.Id,
                PayeeName = bill.PayeeName,
                DueDate = bill.DueDate,
                PaymentDue = bill.PaymentDue,
                Paid = bill.Paid,
                Version = bill.Version,
            };

            await ShowFormDialogAsync($"Edit Bill #{bill.Id}", draft, "Save", SaveEditAsync);
        }

        private async Task ShowFormDialogAsync(
            string title,
            Bill draft,
            string confirmText,
            Func<Bill, Task<bool>> onSave)
        {
            var parameters = new DialogParameters<BillFormDialog>
            {
                { x => x.Bill, draft },
                { x => x.ConfirmText, confirmText },
                { x => x.OnSave, onSave },
            };

            await DialogService.ShowAsync<BillFormDialog>(title, parameters, FormOptions);
        }

        private async Task<bool> SaveNewAsync(Bill bill)
        {
            var result = await BillService.CreateBillAsync(bill);

            if (!result.Success)
            {
                Snackbar.Add(result.ToMessage("create"), Severity.Error);
                return false;
            }

            Snackbar.Add("Bill created", Severity.Success);
            await AfterWriteAsync();
            return true;
        }

        private async Task<bool> SaveEditAsync(Bill bill)
        {
            var result = await BillService.UpdateBillAsync(bill);

            if (!result.Success)
            {
                Snackbar.Add(result.ToMessage("update"), Severity.Error);

                // A 409 means someone else won the race and our copy is stale,
                // and a 404 means the row is gone — in both cases the list on
                // screen is wrong, so refresh it before the user retries.
                if (result.IsConflict || result.IsNotFound)
                {
                    await AfterWriteAsync();
                }

                return false;
            }

            Snackbar.Add("Bill updated", Severity.Success);
            await AfterWriteAsync();
            return true;
        }

        private async Task ConfirmDeleteAsync(Bill bill)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete bill?",
                $"Delete bill #{bill.Id} for {bill.PayeeName} ({bill.PaymentDue.ToString("C")})? "
                + "This cannot be undone.",
                yesText: "Delete",
                cancelText: "Cancel");

            if (confirmed != true)
            {
                return;
            }

            var result = await BillService.DeleteBillAsync(bill.Id);

            if (!result.Success)
            {
                Snackbar.Add(result.ToMessage("delete"), Severity.Error);

                if (result.IsNotFound)
                {
                    await AfterWriteAsync();
                }

                return;
            }

            Snackbar.Add("Bill deleted", Severity.Success);
            await AfterWriteAsync();
        }

        private Task AfterWriteAsync()
        {
            // Publishing is enough: this page subscribes too, so the dashboard
            // recomputes its counters and charts and the table reloads from the
            // one notification — no double fetch. Scoped per circuit, so it
            // never reaches another connected browser.
            BillEventService.NotifyBillsChanged();
            return Task.CompletedTask;
        }
    }
}
