using System.Globalization;
using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace BillsFrontEndBlazor.Pages
{
    public partial class Index : IDisposable
    {
        private const int ChartMonths = 6;

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = default!;

        // LoadStats already fetched the whole list and threw it away after
        // computing three numbers; keeping it is what makes the charts free.
        private List<Bill> _bills = new();

        private bool _isLoading = true;
        private bool _loadFailed;

        private int TotalBills => _bills.Count;
        private int PaidBills => _bills.Count(b => b.Paid);
        private int UnpaidBills => TotalBills - PaidBills;

        private decimal OutstandingAmount => _bills
            .Where(b => !b.Paid)
            .Sum(b => b.PaymentDue);

        private double PaidPercent => TotalBills == 0
            ? 0
            : Math.Round(PaidBills * 100d / TotalBills);

        // MudBlazor 9 feeds every chart type through ChartSeries<T> + ChartLabels;
        // the InputData/InputLabels/XAxisLabels parameters of earlier versions
        // are gone.
        private List<ChartSeries<double>> _statusSeries = new();
        private readonly string[] _statusLabels = { "Paid", "Unpaid" };

        private List<ChartSeries<double>> _monthlySeries = new();
        private string[] _monthLabels = Array.Empty<string>();

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += RefreshDashboard;
            await LoadStatsAsync();
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= RefreshDashboard;
        }

        private void RefreshDashboard()
        {
            // Was `async void`. That form throws on a pooled thread with nobody
            // to catch it if the API is down — a process crash, not just a
            // broken page. InvokeAsync also puts the work back on the circuit's
            // synchronization context, which StateHasChanged requires.
            _ = InvokeAsync(LoadStatsAsync);
        }

        private async Task LoadStatsAsync()
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
                Snackbar.Add("Could not load the dashboard. Is the API running?", Severity.Error);
            }
            finally
            {
                BuildChartData();
                _isLoading = false;
                StateHasChanged();
            }
        }

        private void BuildChartData()
        {
            _statusSeries = new List<ChartSeries<double>>
            {
                new() { Name = "Bills", Data = new double[] { PaidBills, UnpaidBills } },
            };

            // Last ChartMonths months ending with the current one. Built from a
            // fixed month list rather than by grouping, so months with no bills
            // still appear as an empty bar instead of being skipped.
            var firstMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                .AddMonths(-(ChartMonths - 1));

            var months = Enumerable.Range(0, ChartMonths)
                .Select(offset => firstMonth.AddMonths(offset))
                .ToArray();

            _monthLabels = months
                .Select(m => m.ToString("MMM", CultureInfo.CurrentCulture))
                .ToArray();

            var totals = months
                .Select(month => (double)_bills
                    .Where(b => b.DueDate is { } due
                                && due.Year == month.Year
                                && due.Month == month.Month)
                    .Sum(b => b.PaymentDue))
                .ToArray();

            _monthlySeries = new List<ChartSeries<double>>
            {
                new() { Name = "Amount due", Data = totals },
            };
        }
    }
}
