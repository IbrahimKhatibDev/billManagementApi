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

    /// <summary>
    /// Outcome of a batch of writes: the counts, plus the status of the first
    /// one that failed — enough to say why without claiming every failure had
    /// the same cause.
    /// </summary>
    public sealed record BulkPaidResult(BulkPaidOutcome Outcome, HttpStatusCode? FirstFailure)
    {
        public bool Success => Outcome.AllSucceeded;

        public string ToMessage() => Outcome.Describe(Reason);

        private string? Reason => Outcome.AllSucceeded
            ? null
            : FirstFailure switch
            {
                // The list is reloaded after every batch, so the fix is already
                // done by the time this is read — saying so stops the reflex to
                // hit refresh and try the whole thing again.
                HttpStatusCode.Conflict =>
                    "Another change landed first — the list has been refreshed.",
                HttpStatusCode.NotFound =>
                    "They were already gone — the list has been refreshed.",
                HttpStatusCode.Unauthorized =>
                    "Your session has expired. Reload the page to sign in again.",
                _ => "Is the API running?",
            };
    }

    /// <summary>
    /// Every bill matching a query — up to a cap — together with how many there
    /// really are.
    /// <para>
    /// <see cref="TotalCount"/> is the server's answer, not
    /// <c>Bills.Count</c>. Keeping both is what lets the page say "showing the
    /// first 500 of 1,240" instead of quietly presenting 500 as the whole book.
    /// </para>
    /// </summary>
    public sealed record BillBook(List<Bill> Bills, int TotalCount)
    {
        public static BillBook Empty { get; } = new(new List<Bill>(), 0);

        public bool IsCapped => Bills.Count < TotalCount;
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

        /// <summary>
        /// Walks the paged endpoint until the query is exhausted or
        /// <paramref name="cap"/> rows are in hand.
        /// <para>
        /// The Bills page groups by due window, and a due window spans the whole
        /// book — so it can no longer ask for one page at a time. This is the
        /// same walk <see cref="GetAllInRangeAsync"/> does for CSV export, with
        /// two differences: it carries the page's own filter, search and sort
        /// rather than fetching everything, and it stops.
        /// </para>
        /// <para>
        /// The cap is a real bound, not a formality. Without it a 20,000-row
        /// account would render 20,000 rows into a Blazor Server circuit and send
        /// the whole diff over the wire.
        /// </para>
        /// </summary>
        /// <param name="seed">
        /// The query to repeat. Its <c>Page</c> and <c>PageSize</c> are ignored —
        /// the walk sets them.
        /// </param>
        public async Task<BillBook> GetBookAsync(
            BillQuery seed,
            int cap,
            CancellationToken ct = default)
        {
            var all = new List<Bill>();
            var page = 1;
            var total = 0;

            while (true)
            {
                var result = await GetBillsAsync(
                    seed with { Page = page, PageSize = BillQuery.MaxPageSize },
                    ct);

                // Every page carries it, and it is the same number each time; the
                // last one read is as good as the first.
                total = result.TotalCount;
                all.AddRange(result.Items);

                if (all.Count >= cap || !result.HasNext)
                {
                    break;
                }

                page = result.Page + 1;
            }

            // A page is 100 rows, so a cap of 500 lands exactly — but the cap is a
            // constant someone may change, and 501 rows under a cap of 450 would
            // otherwise be handed to the page as if it had asked for them.
            if (all.Count > cap)
            {
                all.RemoveRange(cap, all.Count - cap);
            }

            return new BillBook(all, total);
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
            => SendAsync(() => _http.PostAsJsonAsync(Route, bill, ct), ct);

        public Task<BillWriteResult> UpdateBillAsync(Bill bill, CancellationToken ct = default)
            => SendAsync(() => _http.PutAsJsonAsync($"{Route}/{bill.Id}", bill, ct), ct);

        public Task<BillWriteResult> DeleteBillAsync(long id, CancellationToken ct = default)
            => SendAsync(() => _http.DeleteAsync($"{Route}/{id}", ct), ct);

        /// <summary>
        /// Marks one bill paid given only its id.
        /// <para>
        /// Two round trips, and deliberately so. The Overview's late list is built
        /// from <see cref="SummaryBill"/>, which carries no concurrency token —
        /// that omission is the point of the type, since a report should not be
        /// able to write. Fetching the real bill is how this gets a token, and it
        /// means a bill someone else changed in the meantime comes back as a 409
        /// rather than being silently overwritten.
        /// </para>
        /// <para>
        /// The Bills page does not use this. It already holds every row with its
        /// token, so it writes them directly.
        /// </para>
        /// </summary>
        public async Task<BillWriteResult> MarkPaidAsync(long id, CancellationToken ct = default)
        {
            await AuthorizeAsync();

            Bill? bill;

            try
            {
                bill = await _http.GetFromJsonAsync<Bill>($"{Route}/{id}", ct);
            }
            catch (HttpRequestException ex)
            {
                // Same reason SendAsync catches it: an unhandled exception here
                // tears down the Blazor circuit and the page goes blank.
                return new BillWriteResult(false, ex.StatusCode);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The timeout on the read half of the two round trips. See
                // SendAsync, which covers the write half the same way — this
                // method is only safe to call from an event handler if both are
                // caught, and a hung API times out here first.
                return new BillWriteResult(false, null);
            }

            if (bill is null)
            {
                return new BillWriteResult(false, HttpStatusCode.NotFound);
            }

            bill.Paid = true;

            return await UpdateBillAsync(bill, ct);
        }

        /// <summary>
        /// Marks each of <paramref name="bills"/> paid, and reports how many
        /// landed.
        /// <para>
        /// One request per bill, because the API has no batch endpoint and adding
        /// one would mean inventing a partial-failure semantics on the server
        /// too — a 207 nobody else consumes. The concurrency token on each row
        /// still does its job this way: a bill someone else changed comes back
        /// 409 and is counted as a failure rather than being silently overwritten.
        /// </para>
        /// <para>
        /// Sequential rather than concurrent, deliberately. The API rate-limits
        /// per client, so firing fifty writes at once is the one thing guaranteed
        /// to turn a working batch into a wall of 429s — and a queue of one makes
        /// the failure counts mean what they say.
        /// </para>
        /// <para>
        /// Note that a batch is not a transaction. Writes that succeed before a
        /// failure stay written, which is exactly why the result carries counts
        /// instead of a bool.
        /// </para>
        /// </summary>
        /// <param name="bills">
        /// Bills to mark paid. Already-paid bills should be filtered out by the
        /// caller — sending them would spend a request to write what is already
        /// there.
        /// </param>
        public async Task<BulkPaidResult> MarkManyPaidAsync(
            IReadOnlyList<Bill> bills,
            CancellationToken ct = default)
        {
            var succeeded = 0;
            var failed = 0;
            HttpStatusCode? firstFailure = null;

            foreach (var bill in bills)
            {
                // A copy, not the caller's instance. The page still has these
                // rendered; flipping Paid here would move rows between groups
                // one at a time as the batch ran, and leave them moved if the
                // write was refused.
                var payload = new Bill
                {
                    Id = bill.Id,
                    PayeeName = bill.PayeeName,
                    DueDate = bill.DueDate,
                    PaymentDue = bill.PaymentDue,
                    Paid = true,
                    Version = bill.Version,
                };

                var result = await UpdateBillAsync(payload, ct);

                if (result.Success)
                {
                    succeeded++;
                    continue;
                }

                failed++;

                // First, not last: the first refusal is the one that explains the
                // batch — later ones are often knock-on effects of the same
                // expired session or the same stale list.
                firstFailure ??= result.Status;
            }

            return new BulkPaidResult(new BulkPaidOutcome(succeeded, failed), firstFailure);
        }

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
            Func<Task<HttpResponseMessage>> send,
            CancellationToken ct)
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The HttpClient timeout, which does not raise
                // HttpRequestException — it raises TaskCanceledException, and
                // without this the catch above misses the case it exists for.
                // An API that hangs rather than refusing is the likelier fault
                // of the two, and it took the whole page down with it.
                //
                // Filtered on the token so this only ever swallows the timeout.
                // A caller that cancels its own write is not reporting a failed
                // one, and gets its OperationCanceledException back.
                return new BillWriteResult(false, null);
            }
        }
    }
}
