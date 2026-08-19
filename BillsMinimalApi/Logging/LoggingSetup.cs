using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Templates;

namespace BillsMinimalApi.Logging;

/// <summary>
/// Serilog wiring: a bootstrap logger for startup, the real logger for the app,
/// and one summary line per request instead of the framework's several.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Covers the window between process start and the host being built.
    /// <para>
    /// Until <c>UseSerilog</c> below supplies the real logger, Serilog's static
    /// <c>Log</c> is a silent no-op that drops everything written to it —
    /// including the warnings Serilog itself raises while reading configuration,
    /// which are precisely the ones worth seeing. A bootstrap logger fills that
    /// window and is swapped out, not added to, once the host exists.
    /// </para>
    /// <para>
    /// Program.cs is deliberately not wrapped in a catch-all that logs startup
    /// failures as Fatal. That pattern also swallows the control-flow exceptions
    /// <c>WebApplicationFactory</c> and <c>dotnet ef</c> throw to stop the host
    /// after capturing it, and an unhandled startup exception already reaches
    /// stderr — which is where <c>docker logs</c> reads from anyway.
    /// </para>
    /// </summary>
    public static void CreateBootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

    public static void UseAppSerilog(this WebApplicationBuilder builder) =>
        builder.Host.UseSerilog((context, services, configuration) => configuration
            // Levels come from the "Serilog" section of appsettings.json; sinks
            // are set here. Splitting it that way means the noisy-namespace
            // overrides are editable without a rebuild, while the sink choice —
            // which the format below depends on — cannot drift out from under
            // anything that parses the output.
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                formatter: context.HostingEnvironment.IsDevelopment()
                    ? DevelopmentConsole
                    // Newline-delimited JSON everywhere else, because in
                    // production these lines are read by a log shipper, not a
                    // person, and a regex over a pretty template is how fields
                    // get lost.
                    : new CompactJsonFormatter()));

    /// <summary>
    /// Human-readable, with the correlation id in the line rather than only in
    /// the structured payload — the id is worth little if reading it means
    /// switching to a JSON viewer mid-debug.
    /// <para>
    /// An <see cref="ExpressionTemplate"/> rather than the usual
    /// <c>MessageTemplateTextFormatter</c> because the id is a request-scoped
    /// property: startup, shutdown and EF Core lines have none, and a plain
    /// template renders those as a hole with a space either side. The
    /// <c>{#if}</c> takes the separator with it.
    /// </para>
    /// </summary>
    private static readonly ExpressionTemplate DevelopmentConsole = new(
        "[{@t:HH:mm:ss} {@l:u3}] {#if CorrelationId is not null}{CorrelationId} {#end}{@m}\n{@x}");

    /// <summary>
    /// One line per request — method, path, status, elapsed — replacing the
    /// several the framework emits per request at Information.
    /// </summary>
    public static IApplicationBuilder UseAppRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = GetLevel;

            options.EnrichDiagnosticContext = (diagnostic, http) =>
            {
                // Who, not just what. The ownership model means almost every
                // interesting question about a request starts with whose data it
                // touched, and the sub claim is the same value Bill.OwnerId
                // stores.
                var userId = http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                if (userId is not null)
                {
                    diagnostic.Set("UserId", userId);
                }
            };
        });

    /// <summary>
    /// Demotes the health probes and promotes failures.
    /// <para>
    /// Docker hits <c>/health/ready</c> every ten seconds, forever. At
    /// Information that is 8,640 lines a day saying nothing happened, which
    /// buries the lines that mean something and costs real money once logs are
    /// shipped somewhere that charges by volume. Verbose keeps them available
    /// when the minimum level is lowered to chase a problem, and invisible the
    /// rest of the time.
    /// </para>
    /// </summary>
    private static LogEventLevel GetLevel(HttpContext http, double _, Exception? exception)
    {
        if (exception is not null || http.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        if (http.Request.Path.StartsWithSegments("/health"))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
