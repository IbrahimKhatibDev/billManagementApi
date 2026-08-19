using System.Text.Json;
using BillsMinimalApi.Auth;
using BillsMinimalApi.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace BillsMinimalApi.Tests;

/// <summary>
/// What the readiness probe says about a check that failed, in each of the two
/// environments that answer the question differently.
/// <para>
/// The probe is anonymous and reachable by anyone who can reach the API, so the
/// exception message is the one field on it that has to be handled carefully: an
/// Npgsql connection failure names the host, the port, the database and the login
/// role. In Development that is the point — it is usually the answer to "why will
/// this not start". Anywhere else it is a free map of the infrastructure.
/// </para>
/// <para>
/// Hosts of this class's own, because the branch is on the environment and the
/// shared fixture has exactly one. Each test builds and disposes its own, which
/// is slow enough to be worth doing only twice.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HealthDetailTests : IAsyncLifetime
{
    private const string FailingCheck = "always-fails";

    /// <summary>
    /// Shaped like the thing actually being kept back. Asserting on this exact
    /// string is what makes the Production test meaningful — "not null" would
    /// pass on a message that had merely been reworded.
    /// </summary>
    private const string ConnectionDetail =
        "Failed to connect to 10.0.0.7:5432 as role 'bills_app'.";

    private readonly PostgresApiFixture _fixture;

    public HealthDetailTests(PostgresApiFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        // Production will not start without one: Program.cs generates a random
        // key only in Development, and refuses to boot otherwise rather than
        // ship a predictable default. Comfortably over the 32-byte minimum.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", new string('k', JwtOptions.MinimumKeyBytes * 2));

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Both back to nothing before the next class runs. A leftover
        // ASPNETCORE_ENVIRONMENT of Production would be the more expensive one to
        // leave behind: every host built after this point would demand a signing
        // key and skip Swagger.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_failed_check_does_not_say_why_outside_development()
    {
        var (status, error) = await ProbeAsync(Environments.Production);

        // Still says which check failed, which is the part a caller is entitled
        // to and the part an operator reads first. Only the reason is withheld.
        Assert.Equal(nameof(HealthStatus.Unhealthy), status);
        Assert.Null(error);
    }

    [Fact]
    public async Task A_failed_check_says_exactly_why_in_development()
    {
        var (status, error) = await ProbeAsync(Environments.Development);

        Assert.Equal(nameof(HealthStatus.Unhealthy), status);
        Assert.Equal(ConnectionDetail, error);
    }

    /// <summary>
    /// Boots a host in the given environment with one check that always fails,
    /// and returns what the readiness probe published about it.
    /// </summary>
    private static async Task<(string? Status, string? Error)> ProbeAsync(string environment)
    {
        // Set through the environment rather than WithWebHostBuilder for the
        // reason PostgresApiFixture spells out about the connection string:
        // Program.cs branches on IsDevelopment while the builder is still being
        // configured, which is before anything the factory hands it takes effect.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => services
                .AddHealthChecks()
                // Registered as an instance rather than by type: AddCheck<T>
                // activates through DI, which cannot reach a private nested class.
                .AddCheck(FailingCheck, new AlwaysFails(), tags: [HealthEndpoints.ReadyTag])));

        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var check = document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == FailingCheck);

        return (check.GetProperty("status").GetString(), check.GetProperty("error").GetString());
    }

    /// <summary>
    /// Stands in for Postgres being unreachable. Carrying a real exception
    /// matters: the writer reads <c>Entry.Exception</c>, not the description, so
    /// a check that merely reported Unhealthy would leave the field null in both
    /// environments and the Development test would pass for the wrong reason.
    /// </summary>
    private sealed class AlwaysFails : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy(
                "The database is unreachable.",
                new InvalidOperationException(ConnectionDetail)));
    }
}
