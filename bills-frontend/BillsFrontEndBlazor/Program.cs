using System.Globalization;
using System.Text;
using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// The container's default culture is invariant, where decimal.ToString("C")
// renders "¤42.50" instead of "$42.50". Pin it so currency formatting is the
// same in Docker as it is on a developer machine.
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Both of these are per-circuit state, so both are scoped.
//
// ToastService holds the live toast list and raises OnToastsChanged, which
// ToastHost in MainLayout renders as Bootstrap .toast markup. Scoped means one
// user's toasts never appear in another's browser.
builder.Services.AddScoped<ToastService>();

// Scoped, not singleton. In Blazor Server a singleton is shared across every
// connected circuit, so one user's edit would fire OnBillsChanged on everyone
// else's components — and it would keep strong references to components from
// circuits that have already been torn down. Scoped means per-circuit, which is
// the intended semantics; Index and Bills share a circuit.
builder.Services.AddScoped<BillEventService>();

// AddHttpClient<BillService> registers BillService itself, with a configured
// HttpClient — a separate AddScoped<BillService>() would be redundant and, if it
// came second, would silently win and hand out a client with no BaseAddress.
//
// The URL must end in "/": BillService issues relative requests
// ("restapi/BillDtos"), and Uri resolution drops the last path segment without
// the trailing slash.
var apiBaseUrl = builder.Configuration["BillsApi:BaseUrl"]
    ?? throw new InvalidOperationException(
        "BillsApi:BaseUrl is not configured. Set it in appsettings.json or via the "
        + "BillsApi__BaseUrl environment variable (it must end in '/').");

builder.Services.AddHttpClient<BillService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// No UseHttpsRedirection/UseHsts: there is no TLS anywhere in this stack. HSTS
// in particular is a foot-gun here — running this once in Production mode makes
// the browser cache an HSTS policy for localhost, which then breaks plain HTTP
// for every other localhost project.

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();

// The Reports page's export link. An endpoint rather than a download built in
// the browser: this app has no JS interop anywhere — IJSRuntime is unusable
// during ServerPrerendered's first render — so an <a href> pointing at a
// response with a Content-Disposition is the only way to hand over a file.
//
// The range is re-parsed and re-applied here from the same ReportRanges helper
// the page uses, so an export can never cover a different set of bills than the
// page it was downloaded from.
app.MapGet("/reports/bills.csv", async (
    string? range,
    BillService bills,
    CancellationToken ct) =>
{
    var reportRange = ReportRanges.Parse(range);
    var today = DateTime.Today;

    List<Bill> all;

    try
    {
        all = await bills.GetBillsAsync(ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        // A browser downloading a file has nowhere to show a toast, so this has
        // to be an honest status code rather than an empty CSV that looks like
        // "you have no bills".
        return Results.Problem(
            "Could not reach the bills API.",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var rows = ReportRanges.Filter(all, reportRange, today)
        .OrderBy(b => b.DueDate ?? DateTime.MaxValue)
        .ThenBy(b => b.Id);

    var csv = BillCsvWriter.Write(rows, today);

    // With a BOM: Excel on Windows otherwise decodes the file as the system
    // code page, which mangles any payee name that is not pure ASCII.
    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);

    return Results.File(bytes, "text/csv", $"bills-{reportRange.Slug()}.csv");
});

app.MapFallbackToPage("/_Host");

app.Run();
