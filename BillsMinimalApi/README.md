# BillsMinimalApi — Backend REST API (Minimal API + .NET 10 + PostgreSQL)

Full CRUD over bills, with DTO mapping, EF Core on PostgreSQL, optimistic
concurrency, DataAnnotations validation, Swagger/OpenAPI docs, and Bogus-based
seeding.

The backend is lightweight and framework-agnostic — the repo ships both a Blazor
and a React frontend against it.

## Framework choice

### Why ASP.NET Core Minimal API?

- It provides the simplest way to build REST endpoints.
- Works naturally with EF Core.
- Default project template includes Swagger.
- It's a language I've used before and am familiar with.

## Project structure

```
BillsMinimalApi/
├── Data/
│   ├── AppDbContext.cs           EF model, UTC converters, audit stamping
│   ├── AppDbContextFactory.cs    design-time factory for `dotnet ef`
│   └── DbSeeder.cs               25 Bogus rows when the table is empty
├── Dtos/BillDto.cs               wire contract + validation attributes
├── Endpoints/BillEndPoints.cs    the route group
├── Mappers/BillMappers.cs        DTO ⇄ entity
├── Migrations/                   EF Core migrations (PostgreSQL dialect)
├── Models/bill.cs                the entity
├── BillsMinimalApi.http          ready-made requests for VS Code / Rider
└── Program.cs                    DI, migrate-on-startup, middleware
```

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
`AppDbContext`, and the mappers normalise on the way in. A date with no offset is
interpreted as UTC, not as machine-local time, so a payload behaves identically
inside the container (`TZ=UTC`) and on a developer machine.

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

## Endpoints

Base route:

```
/restapi/BillDtos
```

| Method | Route | Success | Failure |
|---|---|---|---|
| `GET` | `/restapi/BillDtos` | 200 + array | — |
| `GET` | `/restapi/BillDtos/{id}` | 200 | 404 |
| `POST` | `/restapi/BillDtos` | 201 + `Location` | 400 |
| `PUT` | `/restapi/BillDtos/{id}` | 200 | 400, 404, **409** |
| `DELETE` | `/restapi/BillDtos/{id}` | 204 | 404 |

`BillsMinimalApi.http` has a ready-made request for each of these.

### GET — all bills

```csharp
group.MapGet("/", async (AppDbContext db) =>
{
    var bills = await db.Bills.ToListAsync();
    return Results.Ok(bills.Select(BillMapper.ToDto));
});
```

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

Swagger is registered only in the Development environment, which is why
`docker-compose.yml` sets `ASPNETCORE_ENVIRONMENT=Development` on the `api`
service.

### Automated tests

```bash
dotnet test BillsMinimalApi.sln
```

23 integration tests in `../tests/BillsMinimalApi.Tests/`, running against a
throwaway PostgreSQL container. **A running Docker daemon is required.** See the
[root README](../README.md#tests).

## Database seeding

At startup, 25 fake bills are inserted if the table is empty, using
[Bogus](https://github.com/bchavez/Bogus). Due dates are spread from five months
back to one month ahead so the dashboard's monthly chart has something real to
plot.
