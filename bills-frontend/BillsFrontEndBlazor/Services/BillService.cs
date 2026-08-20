using System.Net;
using System.Net.Http.Headers;
using BillsFrontEndBlazor.Models;
using BillsMinimalApi.Contracts;

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

            // The sign-in cookie is given the token's own expiry, so these two
            // die together and a 401 means the session ran out mid-page rather
            // than anything being wrong. Reloading is the fix: the cookie is gone
            // too, so the request lands on the login page.
            HttpStatusCode.Unauthorized =>
                "Your session has expired. Reload the page to sign in again.",

            _ => $"Could not {action} the bill. Is the API running?",
        };
    }

    public class BillService
    {
        private readonly HttpClient _http;
        private readonly ApiTokenAccessor _tokens;
        private bool _tokenAttached;

        // Relative on purpose — the base address is configured once in
        // Program.cs from BillsApi:BaseUrl, which differs between a local
        // `dotnet run` and Docker Compose.
        private const string Route = "restapi/BillDtos";

        public BillService(HttpClient http, ApiTokenAccessor tokens)
        {
            _http = http;
            _tokens = tokens;
        }

        /// <summary>
        /// Puts the caller's bearer token on the client, once.
        /// <para>
        /// Every public method below starts here, because the API is closed by
        /// default — an unauthenticated request gets a 401 from the fallback
        /// policy before it reaches an endpoint.
        /// </para>
        /// <para>
        /// Mutating <c>DefaultRequestHeaders</c> is safe despite the shared-state
        /// look of it: <c>AddHttpClient&lt;BillService&gt;</c> registers this
        /// class as transient and hands each instance its own
        /// <see cref="HttpClient"/> (only the underlying handler is pooled), so
        /// the header is never seen by another user's requests. Two concurrent
        /// calls on one instance can both pass the flag, but they write the same
        /// token, so the race has no losing side.
        /// </para>
        /// </summary>
        private async Task AuthorizeAsync()
        {
            if (_tokenAttached)
            {
                return;
            }

            if (await _tokens.GetTokenAsync() is { Length: > 0 } token)
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            // Set even when no token was found. The alternative is asking again
            // on every call for a user who is not signed in — the answer will not
            // have changed, and the request is going to 401 either way.
            _tokenAttached = true;
        }

        /// <summary>
        /// One page of bills, filtered, searched and sorted by the database.
        /// <para>
        /// The read-only helpers on <see cref="PagedResult{T}"/> (TotalPages,
        /// HasNext, the row numbers) are computed from the four values that do
        /// come over the wire, so they need no wire representation and none is
        /// sent — the JSON carries Items, Page, PageSize and TotalCount.
        /// </para>
        /// </summary>
        public async Task<PagedResult<Bill>> GetBillsAsync(
            BillQuery query,
            CancellationToken ct = default)
        {
            await AuthorizeAsync();

            return await _http.GetFromJsonAsync<PagedResult<Bill>>(
                       $"{Route}?{query.ToQueryString()}", ct)
                   ?? PagedResult<Bill>.Empty(query.Page, query.PageSize);
        }

        /// <summary>
        /// How many bills match a status, without fetching any of them.
        /// <para>
        /// Asks for the smallest page the API will serve and reads TotalCount
        /// off it. That is one row over the wire to answer a question the page
        /// asks on every load — the overdue badge counts the whole table, not
        /// the page you are looking at, so it cannot be derived from the rows.
        /// </para>
        /// </summary>
        public async Task<int> CountAsync(BillStatus status, CancellationToken ct = default)
        {
            var probe = new BillQuery(
                Page: 1,
                PageSize: 1,
                Search: null,
                Status: status,
                Sort: BillSort.Id,
                Descending: false,
                From: null,
                To: null);

            var page = await GetBillsAsync(probe, ct);
            return page.TotalCount;
        }

        /// <summary>
        /// The report aggregates for a due-date window, computed in Postgres.
        /// </summary>
        public async Task<BillSummary> GetSummaryAsync(
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            await AuthorizeAsync();

            var url = $"{Route}/summary{SummaryQuery(from, to)}";
            return await _http.GetFromJsonAsync<BillSummary>(url, ct)
                   ?? new BillSummary();
        }

        /// <summary>
        /// Every bill in a due-date window, oldest due first, walked a page at a
        /// time.
        /// <para>
        /// Only the CSV export needs this: a download is meant to be the whole
        /// range, and paging it would produce a file that quietly stopped after
        /// ten rows. It still goes through the paged endpoint rather than around
        /// it, so the export sees exactly what the list would — and the page size
        /// is the API's own maximum, which is the largest the server will agree
        /// to serve.
        /// </para>
        /// </summary>
        public async Task<List<Bill>> GetAllInRangeAsync(
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            var all = new List<Bill>();
            var page = 1;

            while (true)
            {
                var query = new BillQuery(
                    Page: page,
                    PageSize: BillQuery.MaxPageSize,
                    Search: null,
                    Status: BillStatus.All,
                    Sort: BillSort.DueDate,
                    Descending: false,
                    From: from,
                    To: to);

                var result = await GetBillsAsync(query, ct);
                all.AddRange(result.Items);

                if (!result.HasNext)
                {
                    return all;
                }

                page = result.Page + 1;
            }
        }

        private static string SummaryQuery(DateTime? from, DateTime? to)
        {
            var parts = new List<string>(2);

            if (from is { } start)
            {
                parts.Add($"from={start:yyyy-MM-dd}");
            }

            if (to is { } end)
            {
                parts.Add($"to={end:yyyy-MM-dd}");
            }

            return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
        }

        public Task<BillWriteResult> CreateBillAsync(Bill bill, CancellationToken ct = default)
            => SendAsync(() => _http.PostAsJsonAsync(Route, bill, ct));

        public Task<BillWriteResult> UpdateBillAsync(Bill bill, CancellationToken ct = default)
            => SendAsync(() => _http.PutAsJsonAsync($"{Route}/{bill.Id}", bill, ct));

        public Task<BillWriteResult> DeleteBillAsync(long id, CancellationToken ct = default)
            => SendAsync(() => _http.DeleteAsync($"{Route}/{id}", ct));

        /// <summary>
        /// The server's reading of a line like "Verizon 89.20 fri". Nothing is
        /// created — the caller shows the reading for confirmation and then posts
        /// a real bill through <see cref="CreateBillAsync"/>.
        /// </summary>
        /// <returns>
        /// Null when there is no reading to show: the server was unreachable, it
        /// refused, or a newer keystroke cancelled this call. All three mean the
        /// preview stays as it was.
        /// </returns>
        public async Task<ParsedBill?> ParseBillAsync(string text, CancellationToken ct = default)
        {
            await AuthorizeAsync();

            try
            {
                using var response = await _http.PostAsJsonAsync(
                    $"{Route}/parse", new ParseBillRequest { Text = text }, ct);

                return response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ParsedBill>(ct)
                    : null;
            }
            catch (OperationCanceledException)
            {
                // The caller debounces typing, so a cancelled read is the normal
                // case, not a fault. Rethrowing would surface every superseded
                // keystroke as an unhandled exception and tear down the circuit.
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private async Task<BillWriteResult> SendAsync(
            Func<Task<HttpResponseMessage>> send)
        {
            await AuthorizeAsync();

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
