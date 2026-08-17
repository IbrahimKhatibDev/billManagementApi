using System.Net;
using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Services
{
    /// <summary>
    /// Outcome of a write. A plain bool cannot distinguish "you lost a race,
    /// reload" (409, which the API returns when the concurrency token is stale)
    /// from "bad input" (400) or "it's gone" (404), and the UI needs to say
    /// something different for each.
    /// </summary>
    public sealed record BillWriteResult(bool Success, HttpStatusCode? Status)
    {
        public bool IsConflict => Status == HttpStatusCode.Conflict;

        public bool IsNotFound => Status == HttpStatusCode.NotFound;

        public string ToMessage(string action) => Status switch
        {
            HttpStatusCode.Conflict =>
                "This bill was changed by someone else. Reload and try again.",
            HttpStatusCode.NotFound =>
                "That bill no longer exists. Reload to see the current list.",
            HttpStatusCode.BadRequest =>
                $"Could not {action} the bill: the server rejected the details.",
            _ => $"Could not {action} the bill. Is the API running?",
        };
    }

    public class BillService
    {
        private readonly HttpClient _http;

        // Relative on purpose — the base address is configured once in
        // Program.cs from BillsApi:BaseUrl, which differs between a local
        // `dotnet run` and Docker Compose.
        private const string Route = "restapi/BillDtos";

        public BillService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Bill>> GetBillsAsync(CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<Bill>>(Route, ct)
               ?? new List<Bill>();

        public Task<BillWriteResult> CreateBillAsync(Bill bill, CancellationToken ct = default)
            => SendAsync(() => _http.PostAsJsonAsync(Route, bill, ct));

        public Task<BillWriteResult> UpdateBillAsync(Bill bill, CancellationToken ct = default)
            => SendAsync(() => _http.PutAsJsonAsync($"{Route}/{bill.Id}", bill, ct));

        public Task<BillWriteResult> DeleteBillAsync(long id, CancellationToken ct = default)
            => SendAsync(() => _http.DeleteAsync($"{Route}/{id}", ct));

        private static async Task<BillWriteResult> SendAsync(
            Func<Task<HttpResponseMessage>> send)
        {
            try
            {
                using var response = await send();
                return new BillWriteResult(response.IsSuccessStatusCode, response.StatusCode);
            }
            catch (HttpRequestException)
            {
                // The API is unreachable. Surfaced as a failed result rather than
                // rethrown: in Blazor Server an unhandled exception tears down
                // the circuit and replaces the page with the yellow error bar.
                return new BillWriteResult(false, null);
            }
        }
    }
}
