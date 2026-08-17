# Bills Manager — Blazor Server + Bootstrap 5

A modern, fully featured billing management system built with **Blazor Server**,
**ASP.NET Core Minimal API**, and **Bootstrap 5**. It provides a clean
admin-style UI for managing bills, viewing analytics, and interacting with a
live-updating dashboard.

**Zero external dependencies at runtime.** Bootstrap, Bootstrap Icons and
open-iconic are vendored under `wwwroot/css/`, so the project carries no NuGet
`PackageReference` at all and the browser loads nothing from a CDN — check the
Network tab and you will see no third-party requests. That is what lets the
container run fully self-contained.

There is also no `bootstrap.bundle.js`. Modals are conditional Blazor markup
(`modal fade show d-block` plus a backdrop colour) and toasts work because
Bootstrap hides `.toast` unless it also carries `.show` — both are driven by
component state instead of JavaScript.

---

## 🧭 Frontend options

This project includes a **Blazor Server frontend** by default, offering a modern,
component-driven C# web UI.

Developers who prefer **React** can use the React frontend against the same
backend:

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

Then open <http://localhost:5254>.

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

## 🚀 Features

### 🖥️ Dashboard (home page)

- Live-updating analytics (no timers, event-driven)
- Total bills count
- Paid bills count
- Outstanding amount, currency-formatted
- **Animated counters** that ease into their value on load and after any change
- **Paid vs Unpaid donut chart** and a **monthly totals bar chart** over the last
  six months
- A gradient hero banner, an equal-height Bootstrap card grid, and quick-action
  tiles

Both charts are hand-rolled inline SVG rendered from C# — the donut from a
`stroke-dasharray` arc, the bars from a computed scale in a fixed `viewBox` — so
there is no JS interop, no npm, and nothing fetched over the network. That also
sidesteps the prerender lifecycle traps that bite JS charting libraries in
Blazor Server: `_Host.cshtml` uses `render-mode="ServerPrerendered"`, where
`IJSRuntime` is unusable during the first render.

### 📄 Bills management page

A complete CRUD interface built on a styled Bootstrap table:

- Create / Edit through a shared modal form with validation
- Delete with a confirmation modal
- Inline validation messages
- **Sorting** on ID, Payee, Amount, and Due Date
- **Pagination** with a 10 / 25 / 50 page-size selector and a result count
- **Paid / Unpaid filter**, combined with free-text search on ID or Payee
- **Toast notifications** for create, edit, and delete
- **Loading indicators** during every API call
- Empty-state message when a filter matches nothing
- Errors are caught and surfaced as a toast with a retry button, rather than
  tearing down the circuit

### 📱 Mobile layout

At phone widths the action bar wraps, the modals go full-width, and the table
scrolls inside its own `.table-responsive` container so the page itself never
scrolls sideways.

### 🧭 Sidebar navigation

A gradient sidebar that is persistent on desktop and collapses behind a
hamburger toggle on mobile.

### 🔄 Real-time UI updates

A custom **BillEventService** notifies pages when bills change:

- Live dashboard updates
- Reactive UI
- No polling or timers
- Clean architecture

### 🏗️ Backend API

A RESTful ASP.NET Core Minimal API (`/restapi/BillDtos`) supporting GET, POST,
PUT, and DELETE. See the [API README](../../BillsMinimalApi/README.md).

## 🧩 Tech stack

### Frontend

- **Blazor Server (.NET 10)**
- **Bootstrap 5.1 + Bootstrap Icons**, vendored locally — no NuGet packages
- **Blazor `EditForm` validation**
- **Event-driven updates using a pub/sub service**
- No CDN, no npm, no JavaScript of our own

### Backend

- **ASP.NET Core Minimal API (.NET 10)**
- **HttpClient-based `BillService`**
- **Models shared across UI + API**

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
