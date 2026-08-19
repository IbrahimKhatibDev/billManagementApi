using Serilog.Context;

namespace BillsMinimalApi.Logging;

/// <summary>
/// Gives every request an id that appears on all of its log lines and comes back
/// in the response header.
/// <para>
/// The point is being able to work backwards. A user reports "it failed at about
/// four o'clock"; with a correlation id echoed to the client, the support
/// conversation produces one string that pulls the whole request — including the
/// stack trace that never left the server — out of the log.
/// </para>
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>
    /// Long enough for a GUID or a W3C trace id, short enough that nothing
    /// interesting fits. See <see cref="Sanitize"/>.
    /// </summary>
    private const int MaxLength = 64;

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // An inbound id is honoured so a chain of services shares one, which
            // is the only way the id is worth anything across a call graph. It
            // falls back to ASP.NET's own trace identifier rather than a fresh
            // GUID: that value already exists, already appears in framework
            // diagnostics, and minting a second id for the same request means
            // two ways to name it and no way to join them.
            var id = Sanitize(context.Request.Headers[HeaderName].FirstOrDefault())
                     ?? context.TraceIdentifier;

            // Set on the way out rather than assigned now: headers cannot be
            // touched once the response has started, and something further down
            // the pipeline may well have started it.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = id;
                return Task.CompletedTask;
            });

            // PushProperty rather than an enricher, because the scope is the
            // request: everything logged inside this `using` carries the id,
            // including logs from framework code that knows nothing about it.
            using (LogContext.PushProperty("CorrelationId", id))
            {
                await next();
            }
        });

    /// <summary>
    /// Accepts an inbound id only if it is short and alphanumeric-ish, and
    /// returns <c>null</c> otherwise so the caller falls back to a trusted value.
    /// <para>
    /// This value is attacker-controlled and lands in log output. Unfiltered, a
    /// newline in the header lets a caller forge whole log entries — the classic
    /// log-injection trick of appending something that reads like a legitimate
    /// line from another user. Structured sinks escape it, but the plain-text
    /// console sink used in development does not, and a log nobody can trust to
    /// be verbatim is worse than no log.
    /// </para>
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return null;
        }

        foreach (var c in value)
        {
            // ':' is in the set because the fallback below is ASP.NET's trace
            // identifier, which is "{connection}:{request}". Without it this app
            // would mint ids it then refuses to accept back — and a service that
            // rejects its own output is the sort of thing found at three in the
            // morning rather than in review.
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.' && c != ':')
            {
                return null;
            }
        }

        return value;
    }
}
