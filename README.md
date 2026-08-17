# Bill Management

A bill tracking app: an ASP.NET Core Minimal API over PostgreSQL, a Blazor Server
admin UI built on MudBlazor, and an integration test suite that runs against a
real database. Everything runs in Docker, on Apple Silicon and x86 alike, with no
emulation.

## Architecture

```
browser ──▶ blazor :8080 ──▶ api :8080 ──▶ db :5432
           (Blazor Server)   (Minimal API)  (PostgreSQL 16)
```

The browser only ever talks to the Blazor container. Because this is Blazor
**Server**, every `BillService` call originates *inside* that container and
reaches the API over the Compose network at `http://api:8080/` — the browser
never makes a cross-origin request to the API at all. The API port is published
purely so you can reach Swagger and `curl` from the host.

| Service | Container port | Host port | What it is |
|---|---|---|---|
| `blazor` | 8080 | **5254** | Blazor Server UI (MudBlazor) |
| `api` | 8080 | **5131** | Minimal API + Swagger |
| `db` | 5432 | **5432** | PostgreSQL 16 |

The host ports match the original `launchSettings.json` values, so the same URLs
work whether you are running in Docker or with `dotnet run`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — only needed for `dotnet run` / `dotnet test`
- Docker Desktop (or any Docker engine with Compose v2)

## Run with Docker

```bash
docker compose up --build
```

Then open:

- **UI** — <http://localhost:5254>
- **Swagger** — <http://localhost:5131/swagger>
- **API** — <http://localhost:5131/restapi/BillDtos>

The API applies EF Core migrations on startup and seeds 25 fake bills if the
table is empty, so the UI has data on first load.

To stop and **discard the database volume**:

```bash
docker compose down -v
```

Use `down -v` rather than plain `down` whenever you change a migration — the old
schema otherwise survives in the `pgdata` volume and the new migration fails
against it.

## Run locally

Start only the database in Docker, then run both apps on the host. The default
connection string in `appsettings.json` already points at the published port, so
no extra configuration is needed:

```bash
docker compose up -d db

# terminal 1
dotnet run --project BillsMinimalApi

# terminal 2
dotnet run --project bills-frontend/BillsFrontEndBlazor
```

Same URLs as above. Both projects default to their `http` launch profile, so you
do not need `dotnet dev-certs https --trust`.

> If port 5432 is already taken by a local PostgreSQL, change the `db` port
> mapping in `docker-compose.yml` to `"5433:5432"` and update `Port=` in
> `BillsMinimalApi/appsettings.json` to match.

## Tests

```bash
dotnet test BillsMinimalApi/BillsMinimalApi.sln
```

23 integration tests covering the full API surface — CRUD, optimistic
concurrency, validation, and UTC round-tripping.

**The tests need a running Docker daemon.** They use
[Testcontainers](https://dotnet.testcontainers.org/) to start a throwaway
`postgres:16-alpine` and boot the real host against it, because a fake provider
cannot reproduce the `timestamp with time zone` behaviour that most of this
app's date handling depends on. With Docker down you get a connection error from
the container runtime, not a test failure.

The tests bring up their own container, so you do not need `docker compose up`
first — but there is no conflict if it is already running.

## Project layout

| Path | What |
|---|---|
| `BillsMinimalApi/` | Minimal API, EF Core, migrations, seeder |
| `bills-frontend/BillsFrontEndBlazor/` | Blazor Server UI (MudBlazor) |
| `bills-frontend/FrontEndReact/` | Alternative React frontend |
| `tests/BillsMinimalApi.Tests/` | Integration tests |

## Additional documentation

- [BillsMinimalApi README](./BillsMinimalApi/README.md) — endpoints, database, migrations
- [BillsFrontEndBlazor README](./bills-frontend/BillsFrontEndBlazor/README.md) — UI features, configuration
- [FrontEndReact README](./bills-frontend/FrontEndReact/README.md) — the React alternative

## License

MIT.
