using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The tests the auth work exists for: one user's bills are invisible and
/// untouchable to another.
/// <para>
/// None of them mention the query filter, and that is deliberate. They assert
/// the property — "B's row is not reachable through A's token" — from outside
/// the process, so they would still hold if the enforcement moved somewhere
/// else, and they would fail if a new endpoint forgot it.
/// </para>
/// <para>
/// 404 rather than 403 throughout. 403 answers a question the caller has no
/// business asking: it confirms the id exists. With sequential integer keys that
/// turns a bill list into a census of everybody else's bill count.
/// </para>
/// </summary>
public class BillOwnershipTests : ApiTestBase
{
    public BillOwnershipTests(PostgresApiFixture fixture) : base(fixture)
    {
    }

    private HttpClient Other => Fixture.OtherClient;

    private HttpClient Anonymous => Fixture.AnonymousClient;

    // -- Closed to strangers -------------------------------------------------

    public static TheoryData<string, string> AnonymousRequests() => new()
    {
        { "GET", Routes.Bills },
        { "GET", Routes.Summary },
        { "GET", $"{Routes.Bills}/1" },
        { "POST", Routes.Bills },
        { "PUT", $"{Routes.Bills}/1" },
        { "DELETE", $"{Routes.Bills}/1" },
    };

    [Theory]
    [MemberData(nameof(AnonymousRequests))]
    public async Task Every_bill_route_rejects_a_request_with_no_token(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new BillDto
            {
                PayeeName = "Trespasser Ltd",
                DueDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                PaymentDue = 10m,
            });
        }

        var response = await Anonymous.SendAsync(request);

        // 401, not 404: this one is about the caller, not the resource. The
        // fallback policy in Program.cs answers before routing ever reaches an
        // id, which is why /1 not existing does not change the answer.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_token_is_rejected_like_no_token_at_all()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.Bills);
        request.Headers.Add("Authorization", "Bearer not.a.real.token");

        var response = await Anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -- One user's bill is another user's 404 -------------------------------

    [Fact]
    public async Task Reading_another_users_bill_by_id_returns_404()
    {
        var theirs = await Fixture.CreateBillAsync(payeeName: "Only Theirs", client: Other);

        var response = await Client.GetAsync($"{Routes.Bills}/{theirs.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And the row is genuinely still there — the 404 is scoping, not a bill
        // that failed to be created in the first place.
        Assert.NotNull(await Fixture.ReadEntityAsync(theirs.Id));
    }

    [Fact]
    public async Task Updating_another_users_bill_returns_404_and_changes_nothing()
    {
        var theirs = await Fixture.CreateBillAsync(payeeName: "Only Theirs", client: Other);

        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/{theirs.Id}", new BillDto
        {
            Id = theirs.Id,
            PayeeName = "Renamed By A Stranger",
            DueDate = theirs.DueDate,
            PaymentDue = 999m,
            Paid = true,
            Version = theirs.Version,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await Fixture.ReadEntityAsync(theirs.Id);
        Assert.NotNull(stored);
        Assert.Equal("Only Theirs", stored!.PayeeName);
        Assert.Equal(100.00m, stored.PaymentDue);
        Assert.False(stored.Paid);
    }

    [Fact]
    public async Task Deleting_another_users_bill_returns_404_and_leaves_it_alone()
    {
        var theirs = await Fixture.CreateBillAsync(client: Other);

        var response = await Client.DeleteAsync($"{Routes.Bills}/{theirs.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(await Fixture.ReadEntityAsync(theirs.Id));

        // The owner can still delete it, which is the other half of the claim:
        // the row was protected, not broken.
        var byOwner = await Other.DeleteAsync($"{Routes.Bills}/{theirs.Id}");
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    // -- Collections, not just ids -------------------------------------------

    [Fact]
    public async Task The_list_shows_only_the_callers_bills()
    {
        await Fixture.CreateBillAsync(payeeName: "Mine One");
        await Fixture.CreateBillAsync(payeeName: "Mine Two");
        await Fixture.CreateBillAsync(payeeName: "Theirs One", client: Other);
        await Fixture.CreateBillAsync(payeeName: "Theirs Two", client: Other);
        await Fixture.CreateBillAsync(payeeName: "Theirs Three", client: Other);

        var mine = await Fixture.GetPageAsync();
        var theirs = await Fixture.GetPageAsync(client: Other);

        Assert.Equal(2, mine.TotalCount);
        Assert.All(mine.Items, b => Assert.StartsWith("Mine", b.PayeeName));

        Assert.Equal(3, theirs.TotalCount);
        Assert.All(theirs.Items, b => Assert.StartsWith("Theirs", b.PayeeName));
    }

    [Fact]
    public async Task TotalCount_counts_only_the_callers_bills()
    {
        // TotalCount comes from a separate CountAsync, not from Items.Count, so
        // it is its own chance to miss the filter — and a pager built on a count
        // that includes other people's rows shows empty pages.
        for (var i = 0; i < 12; i++)
        {
            await Fixture.CreateBillAsync(payeeName: $"Theirs {i}", client: Other);
        }

        await Fixture.CreateBillAsync(payeeName: "Mine");

        var mine = await Fixture.GetPageAsync("pageSize=10");

        Assert.Equal(1, mine.TotalCount);
        Assert.Single(mine.Items);
    }

    [Fact]
    public async Task The_summary_aggregates_only_the_callers_bills()
    {
        // The riskiest of the lot: the summary is GroupBy and SUM in Postgres,
        // so a missed filter here does not leak a row, it silently folds someone
        // else's money into your totals.
        await Fixture.CreateBillAsync(payeeName: "Mine", paymentDue: 100m);
        await Fixture.CreateBillAsync(payeeName: "Theirs", paymentDue: 5000m, client: Other);

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(1, summary.BillCount);
        Assert.Equal(100m, summary.TotalBilled);
        Assert.All(summary.Payees, p => Assert.Equal("Mine", p.Payee));
    }

    // -- Ownership is assigned, not asked for --------------------------------

    [Fact]
    public async Task A_new_bill_is_stamped_with_the_callers_id()
    {
        var mine = await Fixture.CreateBillAsync();
        var theirs = await Fixture.CreateBillAsync(client: Other);

        var storedMine = await Fixture.ReadEntityAsync(mine.Id);
        var storedTheirs = await Fixture.ReadEntityAsync(theirs.Id);

        Assert.Equal(Fixture.OwnerId, storedMine!.OwnerId);
        Assert.Equal(Fixture.OtherOwnerId, storedTheirs!.OwnerId);
    }

    [Fact]
    public async Task An_update_cannot_move_a_bill_to_another_owner()
    {
        // BillDto has no OwnerId field, so there is no legitimate way to even
        // attempt this — which is the point. The attempt has to be made as raw
        // JSON, the way a hand-rolled client would, to prove the server ignores
        // a property it never advertised.
        var mine = await Fixture.CreateBillAsync();

        var response = await Client.PutAsJsonAsync($"{Routes.Bills}/{mine.Id}", new
        {
            id = mine.Id,
            ownerId = Fixture.OtherOwnerId,
            payeeName = "Still Mine",
            dueDate = mine.DueDate,
            paymentDue = 42m,
            paid = false,
            version = mine.Version,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await Fixture.ReadEntityAsync(mine.Id);
        Assert.Equal(Fixture.OwnerId, stored!.OwnerId);
        Assert.Equal(42m, stored.PaymentDue);

        // Still visible to its actual owner afterwards. A bill that had drifted
        // to another OwnerId would vanish from this list without any error ever
        // being raised, which is the failure this guards against.
        var page = await Fixture.GetPageAsync();
        Assert.Contains(page.Items, b => b.Id == mine.Id);
    }

    // -- Ids do not collide across users -------------------------------------

    [Fact]
    public async Task Two_users_bills_do_not_share_ids()
    {
        var mine = await Fixture.CreateBillAsync();
        var theirs = await Fixture.CreateBillAsync(client: Other);

        // Worth pinning down: the primary key is a single sequence across the
        // whole table, not per user. If ownership were ever implemented by
        // partitioning ids instead, the 404 tests above would start passing for
        // the wrong reason.
        Assert.NotEqual(mine.Id, theirs.Id);
    }
}
