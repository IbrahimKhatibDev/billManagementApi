using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using BillsFrontEndBlazor.Data;
using MudBlazor.Services;
using BillsFrontEndBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// Mudblazor
builder.Services.AddMudServices();

// API service for bills
builder.Services.AddHttpClient<BillService>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7161/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
