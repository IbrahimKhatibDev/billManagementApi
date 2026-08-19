using System.Text.Json;
using BillsMinimalApi.Data;
using BillsMinimalApi.RateLimiting;
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

    /// <summary>
    /// Both probes live under here, which is what lets the global rate limiter
    /// recognise them without being told about each route separately. Kept as a
    /// constant so that "what is a probe" has one definition rather than a
    /// prefix written out in two files that can drift apart.
    /// </summary>
    public const string BasePath = "/health";

    public const string LivePath = BasePath + "/live";

    public const string ReadyPath = BasePath + "/ready";

    /// <summary>
    /// Whether a request is aimed at a probe. Used by the rate limiter, which
    /// runs before routing has picked an endpoint and so has only the path to go
    /// on.
    /// </summary>
    public static bool IsProbe(PathString path) => path.StartsWithSegments(BasePath);

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
        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            // Runs no checks at all: reaching this line already proves the
            // process is alive and the pipeline is serving, which is the whole
            // of what liveness means. Anything more makes a dependency's outage
            // look like this process's fault.
            Predicate = _ => false,
            ResponseWriter = WriteJson,
        })
        .AllowAnonymous()
        // DisableRateLimiting, and it is load-bearing for the same reason
        // AllowAnonymous is. A throttled probe reads as a failed probe: under
        // exactly the traffic spike a rate limiter exists to survive, Docker
        // would start getting 429s here and restart the container, which is how
        // a slow ten minutes becomes a restart loop. This one costs a status
        // code and touches nothing, so there is no budget worth counting.
        .DisableRateLimiting()
        .WithTags("Health");

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJson,
        })
        .AllowAnonymous()
        // Not exempt, unlike liveness, because this one is not free: every call
        // opens a connection and asks Postgres a question, on an endpoint with
        // no token in front of it. A budget of its own rather than the global
        // one, so that other people's traffic can never spend the orchestrator's
        // — which is the property DisableRateLimiting was protecting, and the
        // whole of what it was protecting. See RateLimitSetup.ReadyPolicy for the
        // size of it.
        .RequireRateLimiting(RateLimitSetup.ReadyPolicy)
        .WithTags("Health");
    }

    /// <summary>
    /// The default writer returns the bare word "Healthy" with no detail, which
    /// tells you nothing at 3am about *which* dependency is down. This writes
    /// the per-check breakdown instead.
    /// </summary>
    private static Task WriteJson(HttpContext http, HealthReport report)
    {
        var services = http.RequestServices;

        // Which check failed is safe to say anywhere. *Why* is not: an Npgsql
        // exception message names the host, the port, the database and the login
        // role, and this endpoint answers anyone who can reach it. In Development
        // that detail is the point — it is usually the answer to "why will this
        // not start" — so the line is drawn at the environment rather than
        // removed entirely.
        var detailed = services.GetRequiredService<IHostEnvironment>().IsDevelopment();

        if (!detailed)
        {
            // Suppressed from the response, not thrown away. The operator still
            // needs it, and the log is where it belongs: correlated by request
            // id, and readable only by someone who can already read the logs.
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HealthEndpoints));

            foreach (var entry in report.Entries.Where(e => e.Value.Exception is not null))
            {
                logger.LogWarning(entry.Value.Exception, "Health check {Check} failed.", entry.Key);
            }
        }

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
                // Null outside Development, where the message has already gone
                // to the log. The status above still says which check failed,
                // which is the part a caller is entitled to.
                error = detailed ? entry.Value.Exception?.Message : null,
            }),
        };

        return http.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
