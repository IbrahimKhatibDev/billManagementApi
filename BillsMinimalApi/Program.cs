using BillsMinimalApi.Data;
using Microsoft.EntityFrameworkCore;
using BillsMinimalApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});


// Add DbContext.
// EnableRetryOnFailure matters here beyond the usual transient-fault case: under
// docker compose the API can win the race against Postgres finishing its
// first-run initialisation even with a healthcheck, and migrations run through
// the execution strategy too.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

var app = builder.Build();

// Apply migrations, then seed. Never EnsureCreated() — it builds the schema
// without recording __EFMigrationsHistory, after which Migrate() fails forever
// with "relation Bills already exists".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: this app is served over plain HTTP (port 8080 in the
// container, 5131 locally) and the middleware would only log a warning per
// request and hand out redirects to a port nothing is listening on.

// Enable CORS middleware
app.UseCors("AllowAll");

// Register Bill endpoints
app.MapBillEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> in the test project can find the
// entry point of this top-level-statements program.
public partial class Program { }
