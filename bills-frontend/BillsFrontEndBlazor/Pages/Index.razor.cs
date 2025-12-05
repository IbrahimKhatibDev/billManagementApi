using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    public partial class Index : IDisposable
    {
        private int TotalBills = 0;
        private int PaidBills = 0;
        private decimal OutstandingAmount = 0;

        [Inject] public BillService BillService { get; set; } = default!;
        [Inject] public BillEventService BillEventService { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            await LoadStats();

            BillEventService.OnBillsChanged += RefreshDashboard;
        }

        private async void RefreshDashboard()
        {
            await LoadStats();
            await InvokeAsync(StateHasChanged);
        }

        private async Task LoadStats()
        {
            var bills = await BillService.GetBillsAsync();

            TotalBills = bills.Count;
            PaidBills = bills.Count(b => b.Paid);
            OutstandingAmount = bills
                .Where(b => !b.Paid)
                .Sum(b => b.PaymentDue);
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= RefreshDashboard;
        }
    }
}
