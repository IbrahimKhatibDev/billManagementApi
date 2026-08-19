namespace BillsMinimalApi.RateLimiting;

/// <summary>
/// The <c>RateLimiting</c> configuration section.
/// <para>
/// The numbers are configuration rather than constants for two reasons. A limit
/// that suits a demo on one box is wrong for anything behind a load balancer,
/// where each instance keeps its own counters and the effective limit is the
/// number written here times the instance count. And the test suite needs to
/// drive a real limiter to its edge without either waiting out a production
/// window or having every other test share the budget.
/// </para>
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Sign-in and registration. Tight, because this is the door.
    /// </summary>
    public WindowOptions Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>
    /// Everything else. Loose enough that a person clicking around never meets
    /// it, tight enough that a script cannot use this API as free compute.
    /// </summary>
    public WindowOptions Global { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    /// <summary>
    /// The readiness probe, which is unauthenticated and costs a round trip to
    /// Postgres per call.
    /// <para>
    /// Sixty a minute is two orders of magnitude above what an orchestrator asks
    /// for — Docker's default healthcheck interval is 30 seconds, Kubernetes'
    /// readiness probe 10 — so no real prober can meet this, while a script
    /// pointed at the endpoint stops being able to open connections faster than
    /// one a second.
    /// </para>
    /// </summary>
    public WindowOptions Ready { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public sealed class WindowOptions
    {
        public int PermitLimit { get; set; }

        public int WindowSeconds { get; set; }

        public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
    }
}
