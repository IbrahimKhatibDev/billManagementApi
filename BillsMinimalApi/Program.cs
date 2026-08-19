using System.Security.Cryptography;
using System.Text;
using BillsMinimalApi.Auth;
using BillsMinimalApi.Data;
using BillsMinimalApi.Endpoints;
using BillsMinimalApi.Logging;
using BillsMinimalApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

LoggingSetup.CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default console logger outright. See LoggingSetup.
builder.UseAppSerilog();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Without this, /swagger can call the endpoints but not authenticate to
    // them, which makes it useless for everything except /auth. With it there is
    // an Authorize button: paste the token from /auth/login once and every
    // subsequent Try It Out carries it.
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Description = "Paste the token returned by /auth/login. No \"Bearer \" prefix needed.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });

    // Takes a factory rather than a value in Swashbuckle 10: the requirement
    // holds a *reference* to the definition above, and a reference needs the
    // document it points into to resolve against.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = [],
    });
});

builder.Services.AddValidation();

// Liveness and readiness probes. See HealthEndpoints for why there are two.
builder.Services.AddAppHealthChecks();

// Register CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// SIGNING KEY
//
// Resolved before anything binds JwtOptions, because both the token service and
// the bearer handler have to agree on it and one of the two branches below
// invents it.
//
// Empty in appsettings.json by design: a signing key in source control is a
// signing key anybody can mint tokens with. Outside Development that is a
// startup failure rather than a warning — an app that silently generates its own
// production key logs everybody out on every deploy, and does it differently on
// every instance behind a load balancer.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var configuredKey = jwtSection[nameof(JwtOptions.SigningKey)];

if (string.IsNullOrWhiteSpace(configuredKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is not configured. Set the Jwt__SigningKey environment "
            + $"variable to a secret of at least {JwtOptions.MinimumKeyBytes} bytes.");
    }

    // Development convenience, with the cost stated: tokens issued before a
    // restart stop validating after it, so `dotnet run` means logging in again.
    // docker-compose.yml sets a fixed key so the demo stack does not do that.
    builder.Configuration["Jwt:SigningKey"] =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(JwtOptions.MinimumKeyBytes));
}
else if (Encoding.UTF8.GetByteCount(configuredKey) < JwtOptions.MinimumKeyBytes)
{
    throw new InvalidOperationException(
        $"Jwt:SigningKey is too short. HMAC-SHA256 needs at least "
        + $"{JwtOptions.MinimumKeyBytes} bytes; this one is "
        + $"{Encoding.UTF8.GetByteCount(configuredKey)}.");
}

builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddSingleton<JwtTokenService>();

var jwtOptions = jwtSection.Get<JwtOptions>()!;

// AUTHENTICATION
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Off, so claims arrive named the way the token spells them. The default
        // mapping rewrites "sub" to a http://schemas.xmlsoap.org/... URI, which
        // then has to be spelled that way everywhere it is read — and, worse,
        // silently returns nothing if you spell it "sub".
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtOptions.SecurityKey(),
            ValidateLifetime = true,

            // Default is five minutes, which means a one-hour token is really a
            // sixty-five-minute one. Thirty seconds still absorbs ordinary clock
            // drift between containers without stretching the lifetime.
            ClockSkew = TimeSpan.FromSeconds(30),

            // So User.Identity.Name is the email rather than empty. With
            // MapInboundClaims off, nothing sets this by default.
            NameClaimType = JwtRegisteredClaimNames.Email,
        };
    });

// AUTHORIZATION
//
// A fallback policy rather than [Authorize]/RequireAuthorization on each group:
// it applies to every endpoint that does not state its own, so an endpoint added
// later is private until somebody deliberately opens it. The /auth group is the
// one that does, with AllowAnonymous.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// IDENTITY
//
// AddIdentityCore, not AddIdentity: the latter wires up cookie authentication
// schemes and an external-login story, none of which a bearer-token API uses.
// This registers UserManager, the password hasher and the validators, which is
// the whole of what /auth needs.
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.User.RequireUniqueEmail = true;

    options.Password.RequiredLength = 8;

    // The one default worth relaxing. A required symbol pushes people towards
    // "Password1!" rather than towards length, and length is what matters.
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>();

// Who the current request belongs to. Scoped to match the DbContext that reads
// it — a singleton here would capture one request's user for the process.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

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
    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, users);
}

// Configure the HTTP request pipeline.

// First, so that everything below runs inside the id's scope and the request
// summary line — written on the way out, after the rest of the pipeline has
// unwound — still carries it.
app.UseCorrelationId();
app.UseAppRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: this app is served over plain HTTP (port 8080 in the
// container, 5131 locally) and the middleware would only log a warning per
// request and hand out redirects to a port nothing is listening on.

// Enable CORS middleware. Before the authentication pair on purpose: a browser's
// preflight OPTIONS carries no Authorization header, so it has to be answered
// here rather than rejected as anonymous by the fallback policy.
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Register endpoints
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapBillEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> in the test project can find the
// entry point of this top-level-statements program.
public partial class Program { }
