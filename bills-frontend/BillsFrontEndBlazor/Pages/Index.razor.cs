using System.Globalization;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The dashboard. Three headline numbers and two charts, all of them read
    /// off a single <see cref="BillSummary"/> for the whole table.
    /// <para>
    /// It used to fetch every bill and count them in C#. Once the list endpoint
    /// started paging, "every bill" would have quietly become the first ten —
    /// and a dashboard that says "Total Bills 10" forever is worse than no
    /// dashboard. The summary endpoint answers the same questions from Postgres
    /// in one round trip.
    /// </para>
    /// </summary>
    public partial class Index : IDisposable
    {
        private const int ChartMonths = 6;

        // Long enough for the browser to paint the collapsed charts before the
        // real values arrive. Both updates are separate SignalR render batches,
        // but with no gap at all the browser can still coalesce them into one
        // style recalculation, and a CSS transition that never sees two frames
        // never runs.
        private const int ChartReplayDelayMs = 60;

        // Matches the donut's stroke-dasharray transition in site.css, so the
        // number in the middle finishes counting exactly as the arc lands.
        private const int PercentFrames = 20;
        private const int PercentDurationMs = 600;

        // Donut geometry. The SVG is authored in these user units and scaled by
        // the browser, so the numbers below are resolution-independent.
        private const double DonutRadius = 70;
        private const double DonutCircumference = 2 * Math.PI * DonutRadius;

        // Bar-chart plot area, in the same viewBox units as the markup.
        private const double BarLeft = 56;
        private const double BarRight = 464;
        private const double BarTop = 24;
        private const double BarBaseline = 258;

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Stands in until the first response lands, so the headline
        /// properties and the charts can read a summary without a null check
        /// apiece.</summary>
        private static readonly BillSummary NoData = new();

        private BillSummary? _summary;

        private bool _isLoading = true;
        private bool _loadFailed;

        /// <summary>The date the figures on screen were computed against — the
        /// server's, from <see cref="BillSummary.AsOf"/>. The bar chart's six
        /// months are counted back from it, so the axis agrees with the totals
        /// plotted on it.</summary>
        private DateTime _today = DateTime.Today;

        private BillSummary Summary => _summary ?? NoData;

        private int TotalBills => Summary.BillCount;
        private int UnpaidBills => Summary.UnpaidCount;
        private int PaidBills => TotalBills - UnpaidBills;

        private decimal OutstandingAmount => Summary.OutstandingAmount;

        /// <summary>
        /// Share of <em>bills</em> paid, which is what the donut and its legend
        /// say. Deliberately not <see cref="BillSummary.PaidPercent"/> — that is
        /// the share of the money, the question the reports page asks.
        /// </summary>
        private double PaidPercent => TotalBills == 0
            ? 0
            : Math.Round(PaidBills * 100d / TotalBills);

        /// <summary>
        /// The donut's paid arc, as an SVG <c>stroke-dasharray</c>: "drawn gap".
        /// The track circle underneath supplies the unpaid remainder.
        /// </summary>
        private string PaidArcDashArray { get; set; } = "0 0";

        private sealed record MonthBar(
            string Label,
            decimal Total,
            double X,
            double Y,
            double Width,
            double Height,
            double CenterX);

        private sealed record GridLine(double Y, string Label);

        private List<MonthBar> _bars = new();
        private List<GridLine> _gridLines = new();

        /// <summary>Incremented on every load so the counters replay even when
        /// the refreshed numbers are identical to the ones already on screen.
        /// See <c>AnimatedCounter.Generation</c>.</summary>
        private int _animationGeneration;

        private bool _chartsReset;

        /// <summary>Suppresses the chart transitions for the one frame that
        /// redraws them at zero — otherwise the collapse animates too, the real
        /// values interrupt it, and nothing visibly grows.</summary>
        private string? ChartResetClass => _chartsReset ? "chart-reset" : null;

        /// <summary>The number inside the donut, counting up alongside its arc.
        /// </summary>
        private double _displayPercent;

        private CancellationTokenSource? _percentCts;

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += RefreshDashboard;
            await LoadStatsAsync();
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= RefreshDashboard;

            // Navigating away mid-count would otherwise leave a timer calling
            // StateHasChanged on a disposed component.
            _percentCts?.Cancel();
            _percentCts?.Dispose();
            _percentCts = null;
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
                // No window: the dashboard is about everything on record, so it
                // asks for the unbounded summary.
                var summary = await BillService.GetSummaryAsync(from: null, to: null);

                _summary = summary;
                _today = summary.AsOf;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _summary = NoData;

                // Back to this machine's date. NoData.AsOf is default(DateTime),
                // and counting six months back from year 1 throws.
                _today = DateTime.Today;
                _loadFailed = true;
                Toasts.ShowError("Could not load the dashboard. Is the API running?");
            }
            finally
            {
                _isLoading = false;
            }

            await ReplayAnimationsAsync();
        }

        /// <summary>
        /// Draws the whole dashboard from empty every time it loads, so pressing
        /// Refresh visibly does something even when nothing changed. The
        /// counters are told to replay via <see cref="_animationGeneration"/>;
        /// the donut arc and the bars animate from CSS transitions, which only
        /// fire if the browser paints the collapsed state first.
        /// </summary>
        private async Task ReplayAnimationsAsync()
        {
            // A second refresh can land while the first is still counting.
            _percentCts?.Cancel();
            _percentCts?.Dispose();
            _percentCts = new CancellationTokenSource();

            _chartsReset = true;
            _displayPercent = 0;
            BuildChartData(collapsed: true);
            StateHasChanged();

            await Task.Delay(ChartReplayDelayMs);

            _chartsReset = false;
            BuildChartData();
            _animationGeneration++;
            StateHasChanged();

            _ = AnimatePercentAsync(PaidPercent, _percentCts.Token);
        }

        private async Task AnimatePercentAsync(double to, CancellationToken ct)
        {
            var interval = TimeSpan.FromMilliseconds(PercentDurationMs / PercentFrames);

            try
            {
                using var timer = new PeriodicTimer(interval);

                for (var frame = 1; frame <= PercentFrames; frame++)
                {
                    await timer.WaitForNextTickAsync(ct);

                    // Ease-out cubic, the same curve AnimatedCounter uses, so
                    // every number on the dashboard settles together.
                    var progress = (double)frame / PercentFrames;
                    var eased = 1 - Math.Pow(1 - progress, 3);

                    _displayPercent = frame == PercentFrames ? to : to * eased;

                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer refresh, or the page went away.
            }
            catch (ObjectDisposedException)
            {
                // The circuit was torn down between frames.
            }
        }

        /// <param name="collapsed">Render the donut arc and the bars at zero,
        /// leaving the axis and labels at their real values so only the data
        /// grows in.</param>
        private void BuildChartData(bool collapsed = false)
        {
            var paidFraction = collapsed || TotalBills == 0
                ? 0
                : (double)PaidBills / TotalBills;

            var arc = DonutCircumference * paidFraction;

            PaidArcDashArray = string.Create(
                CultureInfo.InvariantCulture,
                $"{arc:0.##} {DonutCircumference - arc:0.##}");

            BuildMonthlyBars(collapsed);
        }

        private void BuildMonthlyBars(bool collapsed)
        {
            // Last ChartMonths months ending with the current one. Built from a
            // fixed month list rather than from the response, so months with no
            // bills still appear as an empty slot instead of being skipped — the
            // summary only sends months that have something in them.
            var firstMonth = new DateTime(_today.Year, _today.Month, 1)
                .AddMonths(-(ChartMonths - 1));

            var months = Enumerable.Range(0, ChartMonths)
                .Select(offset => firstMonth.AddMonths(offset))
                .ToArray();

            var billed = Summary.Months.ToDictionary(m => (m.Year, m.Month), m => m.Billed);

            var totals = months
                .Select(month => billed.GetValueOrDefault((month.Year, month.Month)))
                .ToArray();

            // Round the axis up to a whole "nice" number so the gridline labels
            // read as money rather than as the tallest bar's exact value.
            var max = totals.Length == 0 ? 0m : totals.Max();
            var axisMax = NiceAxisMax(max);

            var plotHeight = BarBaseline - BarTop;
            var slot = (BarRight - BarLeft) / months.Length;
            var barWidth = slot * 0.52;

            _bars = months
                .Select((month, i) =>
                {
                    var height = axisMax == 0 || collapsed
                        ? 0
                        : (double)(totals[i] / axisMax) * plotHeight;

                    var x = BarLeft + (slot * i) + ((slot - barWidth) / 2);

                    return new MonthBar(
                        Label: month.ToString("MMM", CultureInfo.CurrentCulture),
                        Total: totals[i],
                        X: x,
                        Y: BarBaseline - height,
                        Width: barWidth,
                        Height: height,
                        CenterX: x + (barWidth / 2));
                })
                .ToList();

            _gridLines = Enumerable.Range(0, 4)
                .Select(step =>
                {
                    var fraction = step / 3d;
                    return new GridLine(
                        Y: BarBaseline - (plotHeight * fraction),
                        Label: (axisMax * (decimal)fraction).ToString("C0", CultureInfo.CurrentCulture));
                })
                .ToList();
        }

        /// <summary>
        /// Rounds up to 1, 2 or 5 times a power of ten — the same ladder chart
        /// libraries use, so the axis labels stay readable at any data scale.
        /// </summary>
        private static decimal NiceAxisMax(decimal max)
        {
            if (max <= 0)
            {
                return 0;
            }

            var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
            var normalised = max / magnitude;

            var rounded = normalised switch
            {
                <= 1m => 1m,
                <= 2m => 2m,
                <= 5m => 5m,
                _ => 10m,
            };

            return rounded * magnitude;
        }

        private static string Svg(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
