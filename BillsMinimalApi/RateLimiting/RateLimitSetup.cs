using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillsMinimalApi.RateLimiting;

/// <summary>
/// Two limiters: a loose one over everything, and a tight one the sign-in
/// endpoints opt into.
/// </summary>
public static class RateLimitSetup
{
    /// <summary>
    /// Applied with <c>RequireRateLimiting</c> on /auth/login and /auth/register.
    /// Deliberately not on the whole /auth group — /auth/me is how a client asks
    /// whether its token is still good, and throttling that at sign-in rates
    /// would punish the ordinary case.
    /// </summary>
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddAppRateLimiter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limits = new RateLimitOptions();
        configuration.GetSection(RateLimitOptions.SectionName).Bind(limits);

        return services.AddRateLimiter(options =>
        {
            // A fixed window for the general case: cheap, and its one weakness —
            // twice the limit can land either side of a window boundary — does
            // not matter when the limit is a courtesy rather than a defence.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.Global.PermitLimit,
                    Window = limits.Global.Window,
                    QueueLimit = 0,
                }));

            // A sliding window for sign-in, where that boundary burst is exactly
            // the thing being prevented: a fixed window lets an attacker fire a
            // full budget at 11:59:59 and another at 12:00:00, doubling the
            // guesses per minute for free. Six segments means the budget frees up
            // ten seconds at a time instead of all at once.
            options.AddPolicy(AuthPolicy, http =>
                RateLimitPartition.GetSlidingWindowLimiter(ClientKey(http), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limits.Auth.PermitLimit,
                    Window = limits.Auth.Window,
                    SegmentsPerWindow = 6,

                    // No queue. Holding a rejected sign-in attempt open makes the
                    // attacker wait rather than go away, and ties up a connection
                    // on this end to do it.
                    QueueLimit = 0,
                }));

            // The longest either budget can take to free up, and so a wait that is
            // never an underestimate whichever of the two did the rejecting. See
            // OnRejected for why the handler needs to be told.
            var longestWindow = limits.Auth.Window > limits.Global.Window
                ? limits.Auth.Window
                : limits.Global.Window;

            options.OnRejected = (context, cancellationToken) =>
                OnRejected(context, longestWindow, cancellationToken);
        });
    }

    /// <summary>
    /// Who a limit is counted against.
    /// <para>
    /// The remote address, which is the caller's own IP only when nothing sits in
    /// front of this app. Behind a reverse proxy or load balancer every request
    /// arrives from the same address and one client's budget becomes everyone's —
    /// so a real deployment has to add <c>UseForwardedHeaders</c> with the proxy
    /// explicitly trusted. Reading <c>X-Forwarded-For</c> without that is worse
    /// than not reading it: the header is caller-supplied, so anyone can spend a
    /// fresh budget per made-up address.
    /// </para>
    /// <para>
    /// Not the user id: this runs before authentication, which is the point.
    /// A limiter that has to validate a token before deciding whether to refuse
    /// has already done the work the refusal was meant to avoid, and the
    /// endpoint that most needs limiting has no authenticated user by definition.
    /// </para>
    /// </summary>
    private static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <param name="longestWindow">
    /// What to say when the limiter will not say. See below.
    /// </param>
    private static ValueTask OnRejected(
        OnRejectedContext context,
        TimeSpan longestWindow,
        CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        // Turns "try again later" into a number, which is the difference between
        // a client that backs off correctly and one that retries in a tight loop
        // against the endpoint that just asked it to stop.
        //
        // The lease is asked first and rarely answers. SlidingWindowRateLimiter
        // is the one limiter in System.Threading.RateLimiting that advertises
        // RETRY_AFTER in its MetadataNames and then never supplies a value —
        // TryGetMetadata returns false regardless of AutoReplenishment, where
        // FixedWindow and TokenBucket both answer. Which makes the sign-in
        // limiter, whose 429s a real client is far likelier to meet, precisely
        // the one that would go out with no header at all.
        //
        // The window is an upper bound rather than the true wait: a sliding
        // window frees one segment at a time, so the real answer is often a sixth
        // of this. Overstating it makes a well-behaved client wait longer than it
        // needed to, which is the safe direction to be wrong in.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLease)
            ? fromLease
            : longestWindow;

        response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Nothing is logged here on purpose. UseSerilogRequestLogging wraps this
        // middleware and already writes one line per request carrying the 429, so
        // a warning here would double every entry — and under the flood this
        // exists to absorb, doubling the log is the wrong direction.
        return new ValueTask(response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = "Too many requests.",
                Detail = "You have made too many requests in a short period. Try again shortly.",
                Status = StatusCodes.Status429TooManyRequests,
            },
            options: null,
            // Spelled out because WriteAsJsonAsync would otherwise say
            // application/json, and every other error this API returns is a
            // problem document. A client that switches on the content type
            // should not find one error shaped differently from the rest.
            contentType: "application/problem+json",
            cancellationToken));
    }
}
