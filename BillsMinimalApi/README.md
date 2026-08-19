# BillsMinimalApi — Backend REST API (Minimal API + .NET 10 + PostgreSQL)

Full CRUD over bills, with DTO mapping, EF Core on PostgreSQL, optimistic
concurrency, DataAnnotations validation, JWT bearer authentication with per-user
data isolation, Swagger/OpenAPI docs, and Bogus-based seeding.

The backend is lightweight and framework-agnostic. The Blazor Server UI is the
frontend it ships with; an earlier React client lives in the repo too and talks
to the same endpoints.

## Framework choice

### Why ASP.NET Core Minimal API?

- It provides the simplest way to build REST endpoints.
- Works naturally with EF Core.
- Default project template includes Swagger.
- It's a language I've used before and am familiar with.

## Project structure

```
BillsMinimalApi/
├── Auth/
│   ├── ICurrentUser.cs           "who is asking", from the JWT's sub claim
│   ├── JwtOptions.cs             the Jwt config section, with a key-length rule
│   └── JwtTokenService.cs        issues the bearer tokens
├── Data/
│   ├── AppDbContext.cs           EF model, UTC converters, audit stamping,
│   │                             the ownership query filter, Identity tables
│   ├── AppDbContextFactory.cs    design-time factory for `dotnet ef`
│   ├── UtcDateTime.cs            the one definition of "what UTC means" here
│   └── DbSeeder.cs               the demo account and its 25 Bogus rows
├── Dtos/
│   ├── AuthDtos.cs               register/login requests, the token response
│   └── BillDto.cs                wire contract + validation attributes
├── Endpoints/
│   ├── AuthEndpoints.cs          /auth/register, /auth/login, /auth/me
│   ├── BillEndPoints.cs          the bills route group
│   └── HealthEndpoints.cs        /health/live, /health/ready
├── Logging/
│   ├── CorrelationId.cs          the per-request id, and the header allowlist
│   └── LoggingSetup.cs           Serilog sinks, levels, request logging
├── Mappers/BillMappers.cs        DTO ⇄ entity
├── Migrations/                   EF Core migrations (PostgreSQL dialect)
├── Models/
│   ├── AppUser.cs                the Identity user
│   └── bill.cs                   the entity
├── Queries/
│   ├── BillQueryable.cs          composable IQueryable filters, search, sort
│   └── BillSummaryBuilder.cs     the report aggregates, computed in Postgres
├── BillsMinimalApi.http          ready-made requests for VS Code / Rider
└── Program.cs                    DI, migrate-on-startup, middleware
```

The request and response shapes for the list and summary endpoints live in
`../BillsMinimalApi.Contracts/`, a small class library with no dependencies that
this project and the Blazor UI both reference. Putting `BillQuery`,
`PagedResult<T>`, `BillSummary` and `ReportRange` there means the client builds
its query string with the same type the server parses it into, so the two cannot
drift apart silently.

## Database

PostgreSQL 16, via `Npgsql.EntityFrameworkCore.PostgreSQL`. Connection string in
`appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=billdb;Username=bills;Password=bills_dev_password"
}
```

That default points at the port `docker-compose.yml` publishes, so `dotnet run`
works against the containerised database with no extra setup. Compose overrides
it for the containerised API via the environment variable:

```
ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=billdb;Username=bills;Password=bills_dev_password
```

The double underscore is ASP.NET Core's separator for nested configuration keys,
so that variable overrides `ConnectionStrings:DefaultConnection`. The credentials
above are a local-development default and are deliberately committed; use a real
secret store for anything else.

### Migrations run on startup

`Program.cs` calls `db.Database.MigrateAsync()` before seeding, so a fresh
database is created and brought up to date whenever the API boots — one code
path for both `docker compose up` and bare `dotnet run`. There is no separate
migration step to remember and no migration sidecar container.

### Dates are always UTC

Npgsql maps `DateTime` to `timestamp with time zone` and **throws** on
`DateTimeKind.Unspecified` — which is exactly what a bare `"2026-03-15"` in a
JSON body deserialises to. Rather than patching each call site, `DueDate`,
`CreateTime` and `UpdateTime` all go through a value converter in
`AppDbContext`, and the mappers normalise on the way in. Both routes call
`UtcDateTime.Normalize`, so there is one rule rather than two that can drift.

That rule: `Unspecified` is treated as *already* UTC, not converted from local
time. A date-only payload carries no timezone, so `ToUniversalTime()` would shift
it by the host offset and store a different instant on a developer's Mac than in
the (UTC) container. `Local` values — which is what Bogus and any non-Zulu offset
produce — are converted properly.

### `Version` and `CreateTime` are server-owned

`AppDbContext` overrides both `SaveChanges` roots and stamps the audit fields:
`CreateTime` and `Version = 1` on insert, `UpdateTime` on update. Anything a
client sends for those fields is ignored. `Id` is ignored on POST too — see the
PUT/POST notes below.

## Setup

### Prerequisites

- .NET SDK 10.0
- Docker Desktop (for PostgreSQL — or point the connection string at your own)
- `dotnet ef` CLI, only if you are changing the schema: `dotnet tool install --global dotnet-ef`

### 1. Clone the repository

```bash
git clone https://github.com/IbrahimKhatibDev/billManagementApi.git
cd billManagementApi
```

### 2. Start PostgreSQL

```bash
docker compose up -d db
```

### 3. Run the API

```bash
dotnet run --project BillsMinimalApi
```

It restores and builds on the way, migrates the database, seeds it, and opens
<http://localhost:5131/swagger>.

To run the API in Docker instead, use `docker compose up --build` from the repo
root — see the [root README](../README.md).

### Changing the schema

```bash
dotnet ef migrations add <Name> --project BillsMinimalApi --context AppDbContext
```

`AppDbContextFactory` supplies the design-time connection string, so `dotnet ef`
does not need to execute `Program.cs` (which now does startup database work) to
resolve the context. It reads `ConnectionStrings__DefaultConnection` from the
environment and falls back to `Host=localhost`.

To start over from an empty database:

```bash
docker compose down -v && docker compose up -d db
```

`down -v` is what removes the `pgdata` volume. Without it the old schema
survives and the new migration fails against it.

## Authentication

ASP.NET Core Identity for the user store, JWT bearer tokens for the wire. Bills
belong to accounts, and an account only ever sees its own.

### Getting a token

```
POST /auth/register   {"email": "...", "password": "..."}   → 200 + token, or 400
POST /auth/login      {"email": "...", "password": "..."}   → 200 + token, or 401
GET  /auth/me                                               → 200 {id, email}, or 401
```

`/auth/register` returns a token rather than a bare 201 — the client's next move
after registering is always to log in, and making it do that round trip twice
buys nothing. Both responses look like:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAtUtc": "2026-08-18T16:15:07Z",
  "email": "demo@billsapp.dev"
}
```

Send it as `Authorization: Bearer <token>` on everything else. Tokens last 60
minutes (`Jwt:LifetimeMinutes`) and carry the user id in `sub` — the id, not the
email, because that is what `Bill.OwnerId` stores and it is the one thing about
an account that never changes.

Login answers "no such account" and "wrong password" identically. Distinguishing
them turns the endpoint into a way to ask whether a given person has an account
here.

### Everything is private by default

`Program.cs` sets an authorization **fallback policy** rather than putting
`RequireAuthorization()` on each group:

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

A fallback policy applies to every endpoint that does not state a policy of its
own, so a route added tomorrow is private until somebody deliberately opens it.
The opposite arrangement — public unless annotated — fails in the direction where
the mistake is silent. `/auth` is the one group carrying `AllowAnonymous`.
(Swagger is middleware rather than a routed endpoint, so the policy does not
reach it either way; it is registered only in Development.)

Identity is registered with `AddIdentityCore` rather than `AddIdentity`, which
would wire up cookie schemes and an external-login story a bearer-token API has
no use for. Its rules: unique email, minimum eight characters, and no required
symbol — a mandatory symbol pushes people towards `Password1!` rather than
towards length, and length is what matters. A rejected registration returns
Identity's own messages as a `ValidationProblemDetails`, because "passwords must
have at least one digit" is more useful than "invalid".

### Ownership is a property of the model

`Bill.OwnerId` is a required FK to the Identity user, and `AppDbContext` scopes
every query against it in one line:

```csharp
bill.HasQueryFilter(b => b.OwnerId == _currentUser.Id);
```

That is a global query filter, so EF appends it to *every* query touching
`Bills` — the paged list, the `CountAsync` behind `totalCount`, the `GroupBy`
in `BillSummaryBuilder`, and the `FindAsync` in the id routes alike. The
practical consequence is the interesting one: **user B asking for user A's bill
gets 404**, not 403, and not because any endpoint checks — the row simply is not
in the set the query returned, so the existing `is null` branch answers. 403
would confirm the bill exists, which is precisely what B should not be able to
learn.

The filter closes over the injected `ICurrentUser` rather than capturing its
value, because EF re-reads it when compiling each query's parameters; capturing
`_currentUser.Id` into a field would freeze the first request's user into the
model for the lifetime of the process.

Writes are stamped, not trusted: `SaveChanges` fills `OwnerId` from the current
user on insert alongside `CreateTime` and `Version`, and marks it unmodified on
update, so ownership cannot be reassigned by sending a different value.

The index is `(OwnerId, Paid, DueDate)` — `OwnerId` leads because after the query
filter *every* query carries an equality predicate on it.

### Configuring the signing key

`Jwt:SigningKey` is deliberately empty in `appsettings.json`: a signing key
committed to a public repository is a signing key everybody has. Supply it as
`Jwt__SigningKey`, at least 32 bytes (HMAC-SHA256 needs a key at least as long as
its output; `Program.cs` checks at startup so you get a configuration error
rather than an exception about key sizes at the first sign-in).

In **Development** only, a missing key is replaced by a random one generated at
startup — convenient for `dotnet run`, at the cost of invalidating every issued
token on restart. `docker-compose.yml` therefore pins a fixed development key so
the demo stack survives a `docker compose restart`. That key is in source control
and is public; outside Development a missing key is a startup failure.

### The demo account

`DbSeeder` creates `demo@billsapp.dev` / `Demo12345` if it does not exist and
gives it the 25 generated bills. The credentials are published on purpose: a
deployed demo nobody can get into is a link, not a demo. It is an ordinary
account with no elevated rights, holding nothing but fake data.

The seeder queries with `IgnoreQueryFilters()` and scopes to the demo user by
hand, which is worth knowing if you touch it. Startup is not a request, so
`ICurrentUser.Id` is null and the ownership filter would match nothing — leaving
"are there any bills?" permanently false and reseeding 25 rows on every boot.

## Endpoints

Bills live under one base route:

```
/restapi/BillDtos
```

| Method | Route | Success | Failure |
|---|---|---|---|
| `POST` | `/auth/register` | 200 + token | 400 |
| `POST` | `/auth/login` | 200 + token | 401 |
| `GET` | `/auth/me` | 200 | 401 |
| `GET` | `/restapi/BillDtos` | 200 + one page | — |
| `GET` | `/restapi/BillDtos/summary` | 200 + aggregates | — |
| `GET` | `/restapi/BillDtos/{id}` | 200 | 404 |
| `POST` | `/restapi/BillDtos` | 201 + `Location` | 400 |
| `PUT` | `/restapi/BillDtos/{id}` | 200 | 400, 404, **409** |
| `DELETE` | `/restapi/BillDtos/{id}` | 204 | 404 |
| `GET` | `/health/live` | 200 | — |
| `GET` | `/health/ready` | 200 | 503 |

Every `/restapi` row also answers **401** without a bearer token — only
`/auth/register`, `/auth/login` and the two `/health` routes are anonymous — and
the three `{id}` routes answer **404** for a bill belonging to somebody else,
rather than 403. See
[Ownership is a property of the model](#ownership-is-a-property-of-the-model).

`BillsMinimalApi.http` has a ready-made request for each of these.

### Health checks

Two probes, because liveness and readiness are different questions and an
orchestrator does different things with the answers:

| Route | Asks | Checks | A failure means |
|---|---|---|---|
| `/health/live` | Is this process working? | nothing | restart the container |
| `/health/ready` | Should traffic come here? | Postgres | pull it from the load balancer, leave it running |

Pointing both at the database is the tempting mistake. If liveness depended on
Postgres, a brief database blip would restart every API instance at once —
turning a recoverable outage into a herd of cold starts hitting a database that
is already unwell. So `/health/live` deliberately runs no checks: answering at
all is the proof.

Both are `AllowAnonymous`, which is load-bearing rather than lax. The fallback
authorization policy closes every endpoint that does not open itself, and
Docker's healthcheck presents no bearer token — so without it the probes answer
401, the `api` container never becomes healthy, and the `blazor` service that
waits on it never starts. `HealthCheckTests` asserts this so the line cannot be
quietly dropped.

The response is JSON with the per-check breakdown, rather than the bare word
`Healthy` the default writer emits:

```json
{
  "status": "Healthy",
  "durationMs": 4.21,
  "checks": [{ "name": "postgres", "status": "Healthy", "durationMs": 4.10, "error": null }]
}
```

`error` carries the exception *message* only. A stack trace on an
unauthenticated endpoint hands the app's internals to anyone who can reach it.

### Logging

Serilog replaces the default console logger, and the framework's several lines
per request are replaced by one summary line carrying the same facts:

```
[20:54:21 INF] my-trace-0001 HTTP GET /restapi/BillDtos responded 401 in 1.57 ms
```

That is the Development format. Everywhere else the same event is compact JSON
on stdout, one object per line, for a log shipper to pick up:

```json
{"@t":"2026-08-19T02:55:08.03Z","@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms","RequestMethod":"GET","RequestPath":"/restapi/BillDtos","StatusCode":401,"Elapsed":2.645,"CorrelationId":"prod-check-1","UserId":"…"}
```

Levels live in `appsettings.json` under `Serilog:MinimumLevel`; sinks live in
`Logging/LoggingSetup.cs`. The `Logging:LogLevel` section is deliberately gone —
with Serilog installed, keeping both means two sets of rules that both apply, and
working out which one silenced a message is a bad afternoon.

**Health probes do not appear above.** Docker polls `/health/ready` every ten
seconds, forever; at `Information` that is 8,640 lines a day saying nothing
happened, burying the ones that mean something and costing real money once logs
are shipped somewhere that charges by volume. `LoggingSetup.GetLevel` drops
`/health` to `Verbose` — available when you lower the level to chase a problem,
invisible otherwise — and promotes anything that threw, or answered 5xx, to
`Error`.

#### Correlation IDs

Every response carries an `X-Correlation-ID` header, and every log line the
request produced carries the same value. That is what makes "it failed at about
four o'clock" answerable: the id the user read off the response pulls the whole
request out of the log, including the stack trace that never left the server.

An inbound `X-Correlation-ID` is honoured so a chain of services shares one id;
otherwise the request falls back to ASP.NET's own trace identifier rather than
minting a second id nothing joins to.

Inbound values are attacker-controlled and land in log output, so they are
accepted only if short (≤ 64 characters) and drawn from letters, digits, `-`,
`_`, `.` and `:`. Anything else is dropped in favour of the trusted fallback.
A bare newline in that header would otherwise let a caller end the current log
line and write one of their own — the classic log-injection trick — and while
the JSON sink escapes it, the plain-text one used in development does not.
`CorrelationIdTests` covers the round trip and the rejections, including the one
easy to get wrong: the ids this app generates contain a colon, so it has to
accept its own output back.

### CORS

`Program.cs` registers an `AllowAll` policy — any origin, header and method. The
Blazor UI does not need it (being Blazor **Server**, its requests originate
server-side, not from the browser), but a browser-based client such as the React
frontend does. `AllowAnyHeader` is what lets its `Authorization` header through;
a policy naming its allowed headers would have to list that one explicitly. It is
a development-time default: narrow it to known origins before this is exposed
anywhere real.

`UseCors` runs before `UseAuthentication`, because a preflight `OPTIONS` carries
no `Authorization` header and would be rejected before it could be answered
otherwise.

### GET — a page of bills

This used to be `db.Bills.ToListAsync()`: every row, every request, with the
client left to filter, sort and paginate the array it got back. That works until
the table is bigger than a screen and stops working shortly after. The filters
now compose into a single SQL statement, and its `LIMIT` is what bounds the
response.

Every parameter is optional:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| `page` | 1-based | `1` | Past the last page, you get the last page — not an empty one |
| `pageSize` | 1–100 | `10` | Clamped, so asking for the whole table is not an option a client has |
| `search` | any text | — | Case-insensitive substring of the payee **or** the id: `2` finds bills 2 and 12 |
| `status` | `all`, `paid`, `unpaid`, `overdue` | `all` | `overdue` means unpaid and due before today |
| `sort` | `id`, `payee`, `dueDate`, `amount`, `paid` | `id` | Always with `id` as a tiebreak |
| `dir` | `asc`, `desc` | `asc` | |
| `from`, `to` | `yyyy-MM-dd` | — | Inclusive due-date window |

Unrecognised values fall back to the default rather than returning 400. These
arrive on a URL anyone can type anything into, and rejecting
`?dir=descending` helps nobody.

```
GET /restapi/BillDtos?page=2&pageSize=10&status=overdue&sort=amount&dir=desc
```

```json
{
  "items": [ { "id": 3, "payeeName": "Graham Group", "dueDate": "2026-08-17T00:00:00Z", "paymentDue": 50.04, "paid": false, "version": 1 } ],
  "page": 2,
  "pageSize": 10,
  "totalCount": 26,
  "totalPages": 3,
  "firstRowNumber": 11,
  "lastRowNumber": 20,
  "hasPrevious": true,
  "hasNext": true
}
```

`totalCount` is the count of everything matching the filters, not of `items` —
without it a client cannot render "page 3 of 12", and "is there a next page"
degenerates into fetching one more row to find out. The last five fields are
computed from the first four, so they cannot disagree with them.

Two details in `Queries/BillQueryable.cs` are worth knowing:

- **The sort always ends with `id`.** Postgres gives no ordering guarantee
  between rows that tie on the sort key, so sorting by `paid` without a
  tiebreak can show the same bill on two consecutive pages and never show
  another. It is the columns with the *fewest* distinct values that break.
- **`search` uses `ILIKE`,** which no index can serve. That is a deliberate
  trade at this size; a trigram index is the fix if the table ever justifies one.

The `IX_Bills_Paid_DueDate` index, added in the `AddBillPaidDueDateIndex`
migration, backs the status filters and the due-date sort.

### GET — the report summary

```
GET /restapi/BillDtos/summary?from=2026-05-18&to=2026-08-18
```

Both bounds are optional; omitting them reports on every bill on record. The
response carries the headline figures (billed, paid, outstanding, overdue, due
within 30 days, average, median, largest), monthly buckets, a payee breakdown,
five aging buckets, five size bands, and a six-bill "pay these next" shortlist —
all aggregated with EF `GroupBy` so the arithmetic happens in Postgres.

It is one endpoint rather than several because every section describes the same
window: split up, the headline figures and the month table could be computed a
second apart and disagree across midnight or a concurrent write. `asOf` is the
date the server computed against, sent back so the client renders the server's
idea of "today" rather than the browser's.

This endpoint is why the list endpoint could start paging safely. A reports page
built on "fetch everything and add it up" would have quietly started reporting
on the first ten bills instead.

### GET — a bill by ID

```csharp
group.MapGet("/{id:long}", async (long id, AppDbContext db) =>
{
    var bill = await db.Bills.FindAsync(id);
    return bill is null ? Results.NotFound() : Results.Ok(BillMapper.ToDto(bill));
});
```

### POST — create a bill

```csharp
group.MapPost("/", async (BillDto dto, AppDbContext db) =>
{
    // ToNewEntity, not ToEntity: a client-supplied Id must never reach the
    // identity column.
    var entity = BillMapper.ToNewEntity(dto);
    db.Bills.Add(entity);
    await db.SaveChangesAsync();

    return Results.Created($"/restapi/BillDtos/{entity.Id}", BillMapper.ToDto(entity));
});
```

`ToNewEntity` drops `Id` deliberately. PostgreSQL's `GENERATED BY DEFAULT AS
IDENTITY` **accepts** an explicit value and leaves the sequence behind, so a
client posting `{"id": 500}` would succeed and every later insert would
eventually collide on a duplicate key.

Example body — `id`, `version`, and the audit fields are all server-owned, so
you can omit them:

```json
{
  "payeeName": "Rogers",
  "dueDate": "2026-01-15",
  "paymentDue": 120.50,
  "paid": false
}
```

Requests are validated (`AddValidation()` in `Program.cs` makes the
`[Required]`/`[Range]` attributes on `BillDto` real): an empty `payeeName` or a
`paymentDue` of zero or less returns **400** with a `ValidationProblemDetails`
body and persists nothing.

### PUT — update a bill, with optimistic concurrency

```csharp
group.MapPut("/{id:long}", async (long id, BillDto dto, AppDbContext db) =>
{
    if (id != dto.Id)
        return Results.BadRequest("ID mismatch");

    var existing = await db.Bills.FindAsync(id);
    if (existing is null)
        return Results.NotFound();

    BillMapper.ApplyEditableFields(dto, existing);

    db.Entry(existing).Property(b => b.Version).OriginalValue = dto.Version;
    existing.Version = dto.Version + 1;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict("The data has changed since your last read.");
    }

    return Results.Ok(BillMapper.ToDto(existing));
});
```

`Version` is the concurrency token. EF builds the `UPDATE`'s `WHERE` clause from
the **original** value of that token, and `FindAsync` loaded it microseconds
ago — so assigning `existing.Version` alone can never conflict. Overwriting
`OriginalValue` with what the *client* believed the version to be is what makes
the check real.

So: send back the `version` you read. If someone else has written since, the
`UPDATE` matches no rows and you get **409 Conflict** — reload and retry. Send a
current `version` and you get 200 with `version` incremented.

### DELETE — remove a bill

```csharp
group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
{
    var existing = await db.Bills.FindAsync(id);
    if (existing is null) return Results.NotFound();

    db.Bills.Remove(existing);
    await db.SaveChangesAsync();

    return Results.NoContent();
});
```

## Testing

### Swagger

With the API running, go to <http://localhost:5131/swagger>. You can view all
endpoints, send test requests, and confirm the CRUD operations work.

Everything under `/restapi` needs a token first, so the order is: `POST
/auth/login` with `demo@billsapp.dev` / `Demo12345`, copy the `token` out of the
response, click the green **Authorize** button, and paste it in. Swagger then
attaches it to every request for the rest of the session. The security scheme is
declared in `AddSwaggerGen` purely so that button exists — without it you would
be reduced to `curl` for anything authenticated.

Swagger is registered only in the Development environment, which is why
`docker-compose.yml` sets `ASPNETCORE_ENVIRONMENT=Development` on the `api`
service.

### Automated tests

```bash
dotnet test BillsMinimalApi.sln
```

126 integration tests in `../tests/BillsMinimalApi.Tests/`, running against a
throwaway PostgreSQL container. Most of them cover the list endpoint's paging,
filtering, searching and sorting, and the summary aggregates, because that is
where the behaviour a caller depends on now lives. The rest cover the auth
rules: registration and login, 401 without a bearer token, and one user getting
**404** rather than 403 on another user's bill — for GET, PUT and DELETE alike,
since a 403 on any one of the three would confirm the row exists — plus the two
things that are invisible until they break: that the health probes answer
without a token, and that a hostile `X-Correlation-ID` never reaches the log.

The fixture registers two accounts once per run and hands out an authenticated
`HttpClient` for each: `Client`, which every pre-existing test already used and
which now simply arrives with a bearer token attached, and `OtherClient`, whose
only job is to be a different owner. `ResetAsync` truncates `Bills` between
tests but deliberately leaves `AspNetUsers` alone — clearing it would invalidate
the two tokens already handed out and force a re-login before every test.
**A running Docker daemon is required.** See the
[root README](../README.md#tests).

## Database seeding

At startup the seeder creates the demo account if it is missing, then gives it
25 fake bills if it owns none, using [Bogus](https://github.com/bchavez/Bogus).
Due dates are spread from five months back to one month ahead so the dashboard's
monthly chart has something real to plot.

The condition is "if the demo user owns none", not "if the table is empty" —
otherwise a single bill created by anyone would suppress seeding for everybody.
See [The demo account](#the-demo-account) for why that count has to be taken
with `IgnoreQueryFilters()`.
