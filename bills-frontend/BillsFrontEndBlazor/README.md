# Bills Manager — Blazor Server + Bootstrap 5

A modern, fully featured billing management system built with **Blazor Server**,
**ASP.NET Core Minimal API**, and **Bootstrap 5**. Three pages — a live dashboard,
a CRUD bills table, and a reports page with CSV export — behind a collapsible
admin sidebar, and behind a sign-in page: bills belong to the account that
created them, so there is nothing to show until you are somebody.

**Zero external dependencies at runtime.** Bootstrap 5.1 and Bootstrap Icons 1.11
are vendored under `wwwroot/css/`, so the project carries no NuGet
`PackageReference` at all and the browser loads nothing from a CDN — check the
Network tab and you will see no third-party requests. That is what lets the
container run fully self-contained.

There is also no `bootstrap.bundle.js`, and no JavaScript of our own anywhere.
Modals are conditional Blazor markup (`modal fade show d-block` plus a backdrop
colour) and toasts work because Bootstrap hides `.toast` unless it also carries
`.show` — both are driven by component state instead of JavaScript. The charts
are hand-rolled inline SVG, and even the CSV download is a plain `<a href>`
rather than a JS-built blob.

That is not incidental. `_Host.cshtml` uses `render-mode="ServerPrerendered"`,
where `IJSRuntime` is unusable during the first render — so anything that needed
JS interop would have to special-case the prerender pass. Nothing here does.

---

## 🧭 Frontend options

This project's frontend is the **Blazor Server** app documented here: a
component-driven C# web UI, and the one wired into `docker-compose.yml`.

An earlier **React** client also lives in the repo, against the same backend:

- [FrontEndReact README](../FrontEndReact/README.md)

## 🚀 Setup & run

The quickest path is Docker, which brings up the database, the API, and this UI
together — see the [root README](../../README.md). To run this project on the
host instead:

### 1. Clone the repository

```bash
git clone https://github.com/IbrahimKhatibDev/billManagementApi.git
cd billManagementApi
```

### 2. Start the database

```bash
docker compose up -d db
```

### 3. Run the backend API

```bash
dotnet run --project BillsMinimalApi
```

Listens on <http://localhost:5131>.

### 4. Run the frontend (Blazor Server)

In a second terminal, from the repo root:

```bash
dotnet run --project bills-frontend/BillsFrontEndBlazor
```

Then open <http://localhost:5254> and sign in with **`demo@billsapp.dev` /
`Demo12345`** — the account the API seeds, which owns the 25 generated bills.

Both projects default to their `http` launch profile, so no HTTPS development
certificate is needed.

## ⚙️ Configuration

One setting, in `appsettings.json`:

```json
"BillsApi": {
  "BaseUrl": "http://localhost:5131/"
}
```

**The trailing slash is required.** `BillService` issues relative requests
(`restapi/BillDtos`), and `new Uri(baseAddress, "restapi/BillDtos")` only
resolves correctly when the base address ends in `/`. Drop it and the app starts
cleanly but every table comes up empty.

Docker Compose overrides the value with `BillsApi__BaseUrl=http://api:8080/`.
The URL is resolved over the Compose network rather than from the browser: this
is Blazor **Server**, so every `BillService` call originates inside this
container. Startup fails fast with a clear message if the setting is missing.

`Program.cs` also pins the culture to `en-US`. The container's default is the
invariant culture, where `decimal.ToString("C")` renders `¤42.50` instead of
`$42.50` — so without the pin every amount in the app formats differently in
Docker than on a developer machine.

## 🔐 Signing in

**Demo account: `demo@billsapp.dev` / `Demo12345`.** The login page prints it,
so nobody has to find this file first. Registering works too, and gets you an
account with no bills — correct, and a duller first look.

Three Razor Pages under `Pages/Account/`, not Blazor components:
`Login`, `Register`, and `Logout`. They have to be pages, because signing in
means calling `HttpContext.SignInAsync`, and a live Blazor circuit has no
`HttpContext` to write a `Set-Cookie` header to. They share `_AccountLayout`,
which has no sidebar — there is nothing to navigate to yet.

`Logout` is a page with a confirmation and a POST rather than a link, so a
prefetch or a stray `GET` cannot sign anyone out.

### Two schemes, one sign-in

The browser and the API want different credentials, so the sign-in produces
both:

- The **browser** gets an encrypted, HttpOnly cookie — something it sends
  automatically on every navigation and cannot read from script.
- The **API** needs the stateless bearer token it issued, which it can validate
  without a session store.

`CookieSignIn` puts the second inside the first, as a claim named
`bills:api_token`. So "who is signed in" and "which token do we send" are
answered by the same object and cannot drift apart — and the JWT never reaches
the browser in a form anything can read.

The cookie is issued with **the token's own expiry**, and `SlidingExpiration` is
off. That setting is load-bearing: sliding would keep renewing the cookie past
the token's life, leaving somebody who looks signed in holding a token the API
has stopped accepting — a session that is authenticated everywhere except where
it needs to be. `SameSite=Lax` rather than `Strict`, so following a bookmark or
an inbound link does not land on the login page while already signed in.

### Reading the token back

`ApiTokenAccessor` finds it, and looks in two places because this app renders in
two worlds:

- **`IHttpContextAccessor`** — the prerender pass and the `/reports/bills.csv`
  endpoint, which are ordinary HTTP requests with no circuit.
- **`AuthenticationStateProvider`** — a live SignalR circuit, which has no
  `HttpContext` at all.

`GetAuthenticationStateAsync` *throws* rather than returning an anonymous user
in a scope that is neither, so the second lookup is wrapped in a `catch` that
treats it as "no token", which is what it is.

A `DelegatingHandler` on the typed client would have been the tidier place for
this and does not work: `IHttpClientFactory` builds handler chains in a scope of
its own, so a handler injecting a scoped circuit service gets a different scope
than the circuit it is serving. Constructor injection into `BillService` resolves
correctly, because the typed client is activated from the calling scope.

`ApiAuthClient` is separate from `BillService` and has its own `HttpClient`, for
the same reason the React client uses bare axios for sign-in: it is the one
caller that must send requests *without* a token, since it is how the token is
obtained.

### What is actually closed

`_Host.cshtml` carries `@attribute [Authorize]`, which covers the whole
component app — every Blazor route renders through that one page. Both routes
into it also say so out loud (`MapRazorPages` for `/`, and
`MapFallbackToPage("/_Host").RequireAuthorization()` for everything else),
because a fallback that renders the entire app is worth being able to check
without opening another file.

The CSV endpoint gets its own `.RequireAuthorization()`: it is a minimal-API
endpoint rather than a page, so `[Authorize]` on `_Host` does not reach it — and
without that line it would be a way to read somebody's whole bill list by URL.

`App.razor` wraps the router in `CascadingAuthenticationState` and uses
`AuthorizeRouteView`, whose `<NotAuthorized>` block is reachable in exactly one
situation: the cookie expired while the circuit was still open. Redirecting from
a component there would be a client-side navigation that never touches the
server's login path, so it says what happened and lets a reload do the work.

The sidebar's account block is an `<AuthorizeView>` with no `<NotAuthorized>`
half — nothing renders that menu unless you are signed in. Sign out is a
`<button>` calling `NavigateTo(..., forceLoad: true)` rather than an anchor:
Blazor's router intercepts same-origin anchor clicks and looks for a matching
*component* route, and `/Account/Logout` is a Razor Page, so a plain link would
render "nothing at this address" without ever asking the server.

## 🚀 Features

### 🖥️ Dashboard (`/`)

![The dashboard: a gradient hero, three counter cards, a paid-vs-unpaid donut, and six months of totals as bars](../../docs/screenshots/dashboard.png)

- Live-updating analytics (no timers, event-driven)
- Total bills count, paid bills count, and a currency-formatted outstanding
  amount
- **Animated counters** that ease into their value on load and after any change
- **Paid vs Unpaid donut chart** and a **monthly totals bar chart** over the last
  six months
- **The whole dashboard replays on every load** — counters count up from zero and
  the charts grow in from collapsed, so pressing Refresh visibly does something
  even when nothing changed
- A gradient hero banner, an equal-height Bootstrap card grid, and quick-action
  tiles linking to Bills, Reports, and straight into the create form
  (`bills?new=true`)

Both charts are hand-rolled inline SVG rendered from C# — the donut from a
`stroke-dasharray` arc, the bars from a computed scale in a fixed `viewBox` — so
there is no JS interop, no npm, and nothing fetched over the network. The bar
axis rounds up to 1, 2 or 5 times a power of ten, the same ladder charting
libraries use, so the gridline labels read as money at any data scale.

Replaying is fiddlier than it looks: a CSS transition only fires if the browser
paints the collapsed state first, so the charts are drawn at zero, given a frame,
then redrawn at their real values.

### 📄 Bills management page (`/bills`)

![The bills table: filter pills with an overdue count badge, search, sortable headers, status badges, and a windowed pager](../../docs/screenshots/bills.png)

A complete CRUD interface built on a styled Bootstrap table:

- Create / Edit through a shared modal form with validation
- Delete with a confirmation modal
- Inline validation messages
- **Sorting** on ID, Payee, Due Date, Amount, and Status
- **Pagination** with a 10 / 25 / 50 page-size selector, a result count, and a
  windowed pager that shows at most five page numbers
- **All / Paid / Unpaid / Overdue filter**, combined with free-text search on ID
  or Payee. The Overdue button carries a red count badge when there are any
- **Inline paid toggle** — the status badge is a real button, so marking a bill
  paid is one click rather than a trip through the edit modal. The flip is
  optimistic and reverts if the write fails
- **Overdue is a first-class state**: unpaid and past due renders red, with a
  tinted row and a relative note ("3 days late", "due tomorrow"). An unpaid bill
  that is not due yet stays grey — the red is saved for bills that are actually
  late
- **Toast notifications** for create, edit, delete, and the inline toggle
- **Loading indicators** during every API call, and a reload that dims the table
  rather than blanking it
- Empty-state message when a filter matches nothing
- Errors are caught and surfaced as a toast plus an inline retry, rather than
  tearing down the circuit

All of that is a server round trip now. Sorting, filtering, searching and paging
used to happen in memory over the full table this page had fetched; they are
query-string parameters on `GET /restapi/BillDtos` today, and Postgres does the
work. Two things follow from the change:

- **The search box is debounced by 300 ms**, and typing returns to page 1. Every
  keystroke is a database query otherwise.
- **Responses can arrive out of order.** A slow page 2 landing after a fast
  page 3 would put the wrong rows on screen, so each load takes a generation
  number and a response that is not the newest is dropped.

Filtered to Overdue, with the tinted rows and relative captions the state earns:

![The bills table filtered to overdue: every row tinted red with a left border, an Overdue badge, and a days-late caption under the due date](../../docs/screenshots/bills-overdue.png)

### 📊 Reports page (`/reports`)

Everything on this page is scoped to one date window, picked from presets — All
time, This year, Last 6 months, Last 3 months, Next 3 months — with a caption
spelling out the exact dates, because "Last 6 months" alone does not say whether
today is in it.

![The reports page: range presets, eight headline figures, an overdue aging table with bars, and a what-to-pay-next list](../../docs/screenshots/reports.png)

- **Eight headline figures**: total billed, paid, outstanding, overdue, largest
  bill, average, median, and due-in-30-days. They count up on load and replay
  whenever the range changes
- **Overdue aging** — unpaid bills bucketed by how late they are (not yet due,
  1–30, 31–60, 61–90, over 90 days), with bars scaled against the biggest bucket
- **What to pay next** — a shortlist of the six most urgent unpaid bills, latest
  first
- **Payee breakdown** — every payee in range, sortable on any column, opening on
  who is owed the most, collapsed to the top ten with a show-all toggle
- **Month by month** — billed, paid, outstanding, and a paid-rate bar per month
- **Bill sizes** — how many bills fall in each amount band, scaled on count
  rather than money so one large bill does not outweigh twenty small ones
- **CSV export** of the current range

![Further down the reports page: month-by-month billed/paid/outstanding with paid-rate bars, and bill-size bands](../../docs/screenshots/reports-charts.png)

Bills with no due date cannot be placed on a timeline, so any bounded window
drops them — and the page says how many it left out rather than quietly
under-reporting.

Presets rather than a pair of date pickers: every section has to agree on one
window, and a free-form range gives a hundred ways to ask a question nobody asks.

### 📥 CSV export

The Export CSV button is a plain `<a href="reports/bills.csv?range=..." download>`
pointing at a Minimal API endpoint mapped in this app's own `Program.cs`. It
re-parses the range through the same `ReportRanges` helper the page uses, so an
export can never cover a different set of bills than the page it was downloaded
from.

That endpoint hands the window to the API rather than filtering a list here: the
list endpoint pages, so "download everything and filter it" would export the
first ten bills. `BillService.GetAllInRangeAsync` walks the pages at the maximum
page size and the database does the filtering and the ordering.

The file is RFC 4180 with CRLF line endings and a UTF-8 BOM — Excel on Windows
otherwise decodes it as the system code page and mangles any payee name that is
not pure ASCII. Dates go out as ISO `yyyy-MM-dd` and amounts as bare numbers, so
a spreadsheet parses and sums them instead of treating `$1,234.56` as text. Payee
names are quoted when they need to be; "Sanford, Turcotte and Farrell" unquoted
would silently shift every later column of that row by one.

### 📱 Mobile layout

At tablet widths the table scrolls inside its own `.table-responsive` container
so the page never scrolls sideways. **Below 768px the rows become cards**
instead: horizontal scrolling used to leave Status and Actions entirely
off-screen on a phone with nothing to say they were there. The cards are the same
markup — each cell renders its own label from a `data-label` attribute via CSS,
so there is one source of truth per row rather than two layouts to keep in sync.

Sorting normally lives in the table header, which the card layout hides, so the
action bar grows a sort dropdown and a direction button below that breakpoint.
Modals go full-width.

<img src="../../docs/screenshots/bills-mobile.png" width="380" alt="The bills list at phone width: a hamburger top bar, filter pills, a sort dropdown, and each bill as a labelled card">


### 🧭 Sidebar navigation

A gradient sidebar with two independent behaviours, because the sensible defaults
at the two breakpoints disagree:

- **Desktop** — always on screen, collapsible to an icon rail. Collapsed, each
  icon's `title` names its destination
- **Mobile** — an overlay drawer that starts closed, opens from a hamburger in
  the top bar, and closes on navigation or on a tap outside

### 🔄 Real-time UI updates

A custom **BillEventService** notifies pages when bills change:

- Live dashboard updates
- Reactive UI
- No polling or timers
- Clean architecture

Every page subscribes, so a bill created on one page updates the others without a
manual reload — and publishing once is enough, because the publishing page is a
subscriber too.

### 🏗️ Backend API

A RESTful ASP.NET Core Minimal API (`/restapi/BillDtos`) supporting GET, POST,
PUT, and DELETE. See the [API README](../../BillsMinimalApi/README.md).

No call from this app passes an owner. Bills are scoped server-side by an EF
Core global query filter, so `BillService` asks for "the bills" and gets the
signed-in user's — and a request for a bill belonging to somebody else comes
back **404**, which `BillService` already handles as "it's gone".

Writes go through `BillService`, which returns a `BillWriteResult` rather than a
bool: the UI has to say something different for "you lost a race, reload" (409,
which the API returns when the concurrency token is stale), "bad input" (400),
"it's gone" (404), and "the API is unreachable". A 409 or 404 also triggers a
refresh, because whatever is on screen is now wrong.

## 🧩 Tech stack

### Frontend

- **Blazor Server (.NET 10)**
- **Bootstrap 5.1 + Bootstrap Icons 1.11**, vendored locally — no NuGet packages
- **Blazor `EditForm` validation**
- **Event-driven updates using a pub/sub service**
- No CDN, no npm, no JavaScript of our own

### Backend

- **ASP.NET Core Minimal API (.NET 10)**
- **HttpClient-based `BillService`**
- **Cookie authentication**, carrying the API's JWT as a claim — Razor Pages for
  sign-in, `[Authorize]` on `_Host` for everything else
- **`BillsMinimalApi.Contracts`** — query and response shapes referenced by both
  this app and the API, so `BillQuery` writes the query string that the endpoint
  parses back into a `BillQuery`

---

## 📡 Event-driven updates

A scoped service notifies components:

```csharp
public class BillEventService
{
    public event Action? OnBillsChanged;
    public void NotifyBillsChanged() => OnBillsChanged?.Invoke();
}
```

Scoped, not singleton. In Blazor Server, scoped means per-circuit — one browser
tab — which is the intended semantics here. As a singleton it would fire
`OnBillsChanged` on every connected circuit and hold references to components
from circuits that had already ended.

`ToastService` is scoped for the same reason: it holds the live toast list and
raises `OnToastsChanged`, which `ToastHost` renders as Bootstrap `.toast` markup
from `MainLayout` — outside `@Body`, so a toast survives navigation between
pages. Toasts dismiss themselves after four seconds.

---

## ✅ Frontend feature roadmap

All shipped:

- [x] Add table sorting (ID, Payee, Amount, Due Date)
- [x] Add pagination for large bill lists
- [x] Add filters (Paid / Unpaid)
- [x] Add toast notifications for Create/Edit/Delete
- [x] Add animated dashboard counters
- [x] Add charts to the dashboard (Paid vs Unpaid, Monthly totals)
- [x] Improve mobile layout for modals and tables
- [x] Add sidebar navigation for a full admin feel
- [x] Add currency formatting to the Amount field
- [x] Add loading indicators during API calls

Since:

- [x] Overdue as a first-class state — filter, count badge, red rows, relative
      due dates
- [x] Inline paid toggle straight from the table
- [x] Card layout for the bills table on phones
- [x] Collapsible desktop sidebar rail
- [x] Dashboard animations replay on every refresh
- [x] Reports page — aging, payee breakdown, month-by-month, size bands
- [x] CSV export of the selected report range
- [x] Sign in, register and sign out — cookie auth carrying the API's bearer
      token, with every page and the CSV endpoint closed to anonymous visitors
