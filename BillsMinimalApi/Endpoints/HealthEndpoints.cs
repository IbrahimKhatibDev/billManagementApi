using System.Text.Json;
using BillsMinimalApi.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BillsMinimalApi.Endpoints;

/// <summary>
/// Liveness and readiness probes.
/// <para>
/// Two endpoints rather than one because they answer different questions and an
/// orchestrator does different things with the answers. Liveness asks "is this
/// process still working?" — a false answer gets the container killed and
/// replaced. Readiness asks "should traffic go here right now?" — a false answer
/// takes it out of the load balancer and leaves it running. Pointing both at the
/// same database check is the classic mistake: a brief Postgres blip would then
/// restart every API instance at once, turning a recoverable outage into a
/// thundering herd of cold starts against a database that is already struggling.
/// </para>
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Marks the checks that gate readiness. <see cref="AddHealthChecks"/> tags
    /// the database check with it; the liveness probe deliberately runs nothing.
    /// </summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            // CanConnectAsync under the hood, which for Npgsql is a round trip
            // to Postgres rather than a look at the pool — so this fails when
            // the database is genuinely unreachable, not merely when this
            // process has no open connection to it.
            .AddDbContextCheck<AppDbContext>("postgres", tags: [ReadyTag]);

        return services;
    }

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous on both, and it is load-bearing. Program.cs sets a
        // fallback authorization policy requiring an authenticated user, which
        // applies to every endpoint that does not state otherwise — including
        // these. Without it the probes answer 401, Docker marks the container
        // unhealthy, and the stack never comes up.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            // Runs no checks at all: reaching this line already proves the
            // process is alive and the pipeline is serving, which is the whole
            // of what liveness means. Anything more makes a dependency's outage
            // look like this process's fault.
            Predicate = _ => false,
            ResponseWriter = WriteJson,
        })
        .AllowAnonymous()
        // DisableRateLimiting on both, and it is load-bearing for the same reason
        // AllowAnonymous is. The probes are exempt from the global limit because
        // a throttled probe reads as a failed probe: under exactly the traffic
        // spike a rate limiter exists to survive, Docker would start getting 429s
        // from /health/live and restart the container, which is how a slow ten
        // minutes becomes a restart loop. Both probes are cheap and neither
        // touches the partition key an attacker controls.
        .DisableRateLimiting()
        .WithTags("Health");

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJson,
        })
        .AllowAnonymous()
        .DisableRateLimiting()
        .WithTags("Health");
    }

    /// <summary>
    /// The default writer returns the bare word "Healthy" with no detail, which
    /// tells you nothing at 3am about *which* dependency is down. This writes
    /// the per-check breakdown instead.
    /// </summary>
    private static Task WriteJson(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            // Milliseconds rather than the TimeSpan's default round-trip format:
            // a probe that is passing but slowing down is worth seeing, and
            // "00:00:00.0123456" does not invite anyone to graph it.
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                // The exception message, not the exception. A stack trace on an
                // unauthenticated endpoint hands out the internals of the app to
                // anyone who can reach it.
                error = entry.Value.Exception?.Message,
            }),
        };

        return http.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
