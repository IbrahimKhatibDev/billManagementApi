using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillsMinimalApi.Contracts;
using BillsMinimalApi.Data;
using BillsMinimalApi.Dtos;
using BillsMinimalApi.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BillsMinimalApi.Tests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container.
/// <para>
/// A real Postgres is not overkill here, it is the whole point. EF's InMemory
/// provider enforces no relational constraints and never runs the migration,
/// and — decisively — neither it nor SQLite has a <c>timestamp with time zone</c>
/// column, so neither can reproduce the Npgsql <see cref="DateTimeKind"/> rule
/// that is the most likely runtime failure in this app. They would green-light
/// the exact bug the suite exists to catch.
/// </para>
/// </summary>
public sealed class PostgresApiFixture : IAsyncLifetime
{
    // Same image tag as docker-compose.yml, so the suite runs against the
    // version the app is actually deployed on.
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>
    /// Authenticated as the primary test user. Every pre-auth test used this
    /// client already, so attaching a token here rather than making each test
    /// log in kept the whole existing suite unchanged.
    /// </summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>
    /// A second, unrelated account. The point of the isolation tests: data
    /// arranged through this client must be invisible through
    /// <see cref="Client"/>, and vice versa.
    /// </summary>
    public HttpClient OtherClient { get; private set; } = default!;

    /// <summary>
    /// No <c>Authorization</c> header at all — for asserting that endpoints are
    /// closed rather than merely scoped.
    /// </summary>
    public HttpClient AnonymousClient { get; private set; } = default!;

    /// <summary>Identity id of the user behind <see cref="Client"/>.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    /// <summary>Identity id of the user behind <see cref="OtherClient"/>.</summary>
    public string OtherOwnerId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Must be set before the factory is constructed. Program.cs reads the
        // connection string eagerly inside AddDbContext, well before
        // builder.Build() — so UseSetting/ConfigureAppConfiguration on the
        // factory would land too late to be seen.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            _container.GetConnectionString());

        // Rate limiting effectively off for the shared host, for the same
        // before-the-factory reason.
        //
        // Under TestServer there is no socket, so Connection.RemoteIpAddress is
        // null and every request in the suite falls into the limiter's one
        // "unknown" partition — the whole run shares a single budget. Several
        // hundred requests arrive in a couple of seconds, so the production
        // limits would start returning 429 partway through and fail whichever
        // tests happened to be running at the time. Which ones would depend on
        // ordering and machine speed, which is the worst kind of failure to own.
        //
        // RateLimitTests builds its own host with real limits instead, so the
        // limiter is still exercised — just not by every test at once.
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", "1000000");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "1000000");

        _factory = new WebApplicationFactory<Program>();

        // Booting the host runs MigrateAsync() and DbSeeder for real, so the
        // migration and the seeder's UTC handling are exercised on every run
        // before a single test body executes.
        AnonymousClient = _factory.CreateClient();

        // Registered once for the whole run rather than per test: ResetAsync
        // truncates Bills and leaves AspNetUsers alone, so these two accounts and
        // their tokens stay valid throughout. Registering per test would add a
        // password hash — deliberately expensive — to every single one.
        //
        // The API signs with a key generated at startup (Development, no
        // Jwt__SigningKey set), which is exactly why the tokens are minted here
        // from the running host instead of being constructed by the test.
        (Client, OwnerId) = await RegisterAsync("user-a@tests.local");
        (OtherClient, OtherOwnerId) = await RegisterAsync("user-b@tests.local");
    }

    private async Task<(HttpClient Client, string UserId)> RegisterAsync(string email)
    {
        var response = await AnonymousClient.PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "Test-Password-1",
        });

        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        // Read back from /auth/me rather than decoding the token: it is the id
        // the API itself puts on OwnerId, so a test comparing against it is
        // comparing against the server's own answer.
        var me = await client.GetFromJsonAsync<MeResponse>("/auth/me");

        return (client, me!.Id);
    }

    /// <summary>
    /// Shape of <c>GET /auth/me</c>, which returns an anonymous type on the
    /// server side and so has nothing in the API to deserialize into.
    /// </summary>
    private sealed record MeResponse(string Id, string Email);

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Clears the table so each test starts from a known state. The seeder's 25
    /// random rows prove the host boots and nothing else, so tests arrange their
    /// own data.
    /// <para>
    /// Bills only — the two registered accounts have to outlive this, or the
    /// tokens handed out in <see cref="InitializeAsync"/> would name users that
    /// no longer exist. CASCADE is safe for the same reason it is unnecessary:
    /// it truncates tables holding foreign keys <em>into</em> Bills, and nothing
    /// does. Bills points at AspNetUsers, not the reverse.
    /// </para>
    /// </summary>
    public async Task ResetAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE \"Bills\" RESTART IDENTITY CASCADE");
    }

    /// <summary>
    /// Reads a bill straight out of the database, bypassing the API — the audit
    /// columns the tests assert on are server-owned and not on the DTO.
    /// <para>
    /// <c>IgnoreQueryFilters</c> is load-bearing. This runs in a scope of its own
    /// with no HTTP request behind it, so <c>ICurrentUser.Id</c> is null and the
    /// ownership filter would match nothing — every call would return null and
    /// every assertion built on it would fail for a reason having nothing to do
    /// with what the test is about. Ignoring the filter is also what lets a test
    /// assert on <see cref="Bill.OwnerId"/> at all.
    /// </para>
    /// </summary>
    public async Task<Bill?> ReadEntityAsync(long id)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Bills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>
    /// Arranges one bill through the API rather than the DbContext, so it takes
    /// the same mapping and stamping path the tests are asserting against.
    /// </summary>
    /// <param name="client">
    /// Whose bill it is. Defaults to the primary user; pass
    /// <see cref="OtherClient"/> to arrange data the primary user must not see.
    /// </param>
    public async Task<BillDto> CreateBillAsync(
        string payeeName = "Acme Corp",
        decimal paymentDue = 100.00m,
        bool paid = false,
        DateTime? dueDate = null,
        HttpClient? client = null)
    {
        var response = await (client ?? Client).PostAsJsonAsync(Routes.Bills, new BillDto
        {
            PayeeName = payeeName,
            DueDate = dueDate ?? new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentDue = paymentDue,
            Paid = paid,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BillDto>())!;
    }

    /// <summary>
    /// Fetches one page of the list endpoint. The query string is written out
    /// literally rather than built from <see cref="BillQuery"/>, so a test
    /// exercises the same parsing a browser would rather than the round trip of
    /// the type that does the parsing.
    /// </summary>
    public async Task<PagedResult<BillDto>> GetPageAsync(string? query = null, HttpClient? client = null)
    {
        var url = string.IsNullOrEmpty(query) ? Routes.Bills : $"{Routes.Bills}?{query}";
        return (await (client ?? Client).GetFromJsonAsync<PagedResult<BillDto>>(url))!;
    }

    public async Task<BillSummary> GetSummaryAsync(string? query = null, HttpClient? client = null)
    {
        var url = string.IsNullOrEmpty(query) ? Routes.Summary : $"{Routes.Summary}?{query}";
        return (await (client ?? Client).GetFromJsonAsync<BillSummary>(url))!;
    }
}

public static class Routes
{
    public const string Bills = "/restapi/BillDtos";

    public const string Summary = Bills + "/summary";
}

/// <summary>
/// Every test class joins this collection, so xUnit runs them serially and the
/// single shared container is never truncated out from under a test in another
/// class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Truncates the table before every test. xUnit builds a fresh instance of the
/// test class per test, so <see cref="InitializeAsync"/> runs per test, not per
/// class — stricter isolation than the collection alone provides.
/// </summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestBase(PostgresApiFixture fixture) => Fixture = fixture;

    protected PostgresApiFixture Fixture { get; }

    protected HttpClient Client => Fixture.Client;

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
