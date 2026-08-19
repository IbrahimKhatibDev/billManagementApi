using System.Globalization;
using System.Text;
using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;

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

// The one client that must not carry a bearer token, because it is how one is
// obtained. Same base address, separate registration.
builder.Services.AddHttpClient<ApiAuthClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

// AUTHENTICATION
//
// Cookies here, bearer tokens at the API. Two schemes rather than one because
// the two sides have different problems: the browser needs something it can
// send automatically on every navigation and cannot read from script, and the
// API needs something stateless it can validate without a session store. The
// JWT rides inside the cookie as a claim, so signing in once produces both.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BillsManager.Auth";
        options.Cookie.HttpOnly = true;

        // Lax, not Strict: Strict would drop the cookie on any inbound link,
        // so following a bookmark would land on the login page even while
        // signed in. Lax still refuses to send it on cross-site POSTs, which is
        // the case that matters.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";

        // Off, and this is the load-bearing setting. Each cookie is issued with
        // the JWT's own expiry (see CookieSignIn), so the two die together.
        // Sliding expiration would keep renewing the cookie past the token's
        // life, leaving a user who looks signed in holding a token the API has
        // stopped accepting — a session that is authenticated everywhere except
        // where it needs to be.
        options.SlidingExpiration = false;
    });

builder.Services.AddAuthorization();

// Scoped, because it answers "who is this request/circuit for". Both of the
// places it looks — HttpContext and the circuit's AuthenticationStateProvider —
// are themselves per-user.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiTokenAccessor>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();

// Login, register and sign-out. Previously absent, which also meant the
// UseExceptionHandler("/Error") above had nothing to re-execute to —
// MapFallbackToPage sends unmatched paths to _Host, not to the page named in the
// path, so /Error rendered the Blazor app's "nothing at this address".
app.MapRazorPages();

// The Reports page's export link. An endpoint rather than a download built in
// the browser: this app has no JS interop anywhere — IJSRuntime is unusable
// during ServerPrerendered's first render — so an <a href> pointing at a
// response with a Content-Disposition is the only way to hand over a file.
//
// The range is re-parsed here from the same ReportRanges helper the page uses,
// so an export can never cover a different set of bills than the page it was
// downloaded from.
//
// The window is then handed to the API rather than applied to a full list here:
// the list endpoint pages, so "download everything and filter it" would export
// the first ten bills. GetAllInRangeAsync walks the pages and the database does
// the filtering and the ordering.
app.MapGet("/reports/bills.csv", async (
    string? range,
    BillService bills,
    CancellationToken ct) =>
{
    var reportRange = ReportRanges.Parse(range);
    var today = DateTime.Today;
    var (from, to) = reportRange.Window(today);

    List<Bill> rows;

    try
    {
        rows = await bills.GetAllInRangeAsync(from, to, ct);
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

    var csv = BillCsvWriter.Write(rows, today);

    // With a BOM: Excel on Windows otherwise decodes the file as the system
    // code page, which mangles any payee name that is not pure ASCII.
    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);

    return Results.File(bytes, "text/csv", $"bills-{reportRange.Slug()}.csv");
})
// Not covered by the [Authorize] on _Host: this is a minimal-API endpoint, not a
// page. Without it, the export is a way to read somebody's whole bill list by
// URL — and it would fail anyway, since with no cookie there is no token to pass
// on to the API.
.RequireAuthorization();

// _Host carries [Authorize] itself, so both routes into it — "/" via
// MapRazorPages and everything else via this fallback — are closed. Stated here
// as well because a fallback that renders the entire app is the one route worth
// being able to see the answer for without opening another file.
app.MapFallbackToPage("/_Host").RequireAuthorization();

app.Run();
