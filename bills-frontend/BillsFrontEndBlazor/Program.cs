using System.Globalization;
using BillsFrontEndBlazor.Services;
using MudBlazor;
using MudBlazor.Services;

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

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.PreventDuplicates = false;
});

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
app.MapFallbackToPage("/_Host");

app.Run();
