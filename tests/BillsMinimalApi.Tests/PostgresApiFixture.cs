using System.Net.Http.Json;
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

    public HttpClient Client { get; private set; } = default!;

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

        _factory = new WebApplicationFactory<Program>();

        // Booting the host runs MigrateAsync() and DbSeeder for real, so the
        // migration and the seeder's UTC handling are exercised on every run
        // before a single test body executes.
        Client = _factory.CreateClient();
    }

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
    /// </summary>
    public async Task<Bill?> ReadEntityAsync(long id)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>
    /// Arranges one bill through the API rather than the DbContext, so it takes
    /// the same mapping and stamping path the tests are asserting against.
    /// </summary>
    public async Task<BillDto> CreateBillAsync(
        string payeeName = "Acme Corp",
        decimal paymentDue = 100.00m,
        bool paid = false,
        DateTime? dueDate = null)
    {
        var response = await Client.PostAsJsonAsync(Routes.Bills, new BillDto
        {
            PayeeName = payeeName,
            DueDate = dueDate ?? new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentDue = paymentDue,
            Paid = paid,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BillDto>())!;
    }
}

public static class Routes
{
    public const string Bills = "/restapi/BillDtos";
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
