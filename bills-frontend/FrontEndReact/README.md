# Frontend – Bills App (React + Vite)

This React app connects to the backend Minimal API to load, create, edit, and
delete bills. Bills are per-owner at the API, so it signs in first and every
request after that carries a bearer token.

> **This is not the maintained frontend.** The UI that ships with this project is
> the [Blazor Server app](../BillsFrontEndBlazor/README.md) — dashboard, reports,
> CSV export — and it is the one that has kept pace with the API. This React
> client does plain CRUD against the same endpoints, plus the paging and search
> the list endpoint now requires of any client; everything past that is in the
> Blazor app. It is in `docker-compose.yml` so that
> `docker compose up` brings up something you can click, not because it is being
> developed alongside the API.

## Why I Chose React
I chose React because it is simple, widely used, and easy for building. I've also just used it before for classes as well as personal projects so im familiar with it.

### Requirements
Node.js 20+
NPM

## Setup and Run Instructions

### In Docker

From the repo root, this app comes up with everything else:

```
docker compose up --build
```

Then open <http://localhost:5173> and sign in with **`demo@billsapp.dev` /
`Demo12345`**. The `react` service builds the bundle with Node and serves the
result with nginx, so the shipped image contains no toolchain — just static
files.

### On the host

Start the backend first (this needs a database, so see the
[root README](../../README.md)):

```
docker compose up -d db api
```

Then, in this folder:

```
npm install
npm run dev
```

Frontend: http://localhost:5173  
Backend API: http://localhost:5131

Same port either way, so the URL does not change between the two.

Then sign in — see [Signing in](#signing-in) below.

## API Base URL

`src/api/client.js` reads `VITE_API_BASE_URL` and falls back to
`http://localhost:5131`, which is where the API listens both under Docker and
under `dotnet run` — so out of the box there is nothing to configure.

That is the API **root**, not the bills collection. It was the collection until
there was a second family of endpoints — `/auth/*` — to reach as well; each
module appends its own path.

To point it somewhere else, copy `.env.example` to `.env` and edit it:

```
VITE_API_BASE_URL=http://localhost:5131
```

Two things worth knowing:

- **Vite inlines `VITE_*` variables at build time.** They are not read at
  runtime, so changing `.env` after `npm run build` does nothing until you
  rebuild. In Docker the value is a build arg, set on the `react` service in
  `docker-compose.yml` rather than as a container environment variable.
- **It must be a URL the browser can reach.** This is a static SPA, so the
  request comes from the browser, not from the container — `http://api:8080`
  works only inside the compose network. That is the opposite of the Blazor app,
  which calls the API from the server side and does use the compose name.

The API sets `AllowAnyOrigin`, so these cross-origin calls work without further
setup — and `AllowAnyHeader`, which is the part that matters once there is a
token: `Authorization` is not a header CORS lets through by default. It also
exposes `Retry-After` and `X-Correlation-ID`, which is the same rule pointed the
other way: without that, a response header this client wants to read is on the
wire and visible in DevTools but absent from the object axios hands back.

## Signing in

There is no route table and no `/login` URL. `App.jsx` reads the session store
and renders `<SignIn />` instead of the bills table when there is no token, so
the gate is a branch rather than a redirect — a router would be two screens and
a dependency for a two-state app.

**The demo account is `demo@billsapp.dev` / `Demo12345`**, and the sign-in form
prints it. The account is seeded by the API and owns the 25 generated bills;
registering instead gets you a working but empty table.

The same form registers, on a toggle:

- **Sign in** — `POST /auth/login`, which answers 401 for a wrong password *or*
  an unknown email, with the same message either way.
- **Create an account** — `POST /auth/register`, which returns a token too, so a
  new account lands on the table rather than back at a login form. The confirm
  box is checked here, not by the API, which knows nothing about a second
  password field.

Failures are shown as a list because Identity's password rules come back as a
`ValidationProblemDetails` `errors` dictionary — several sentences, not one.

### Where the token lives

`src/api/session.js` keeps it in `localStorage`, readable by any script on this
origin. That is the accepted trade for a static SPA: the alternative, an
HttpOnly cookie, needs a server of its own to set — which is exactly what this
client does not have, and exactly what the Blazor front end next door does have
and does use.

The store is three keys and a `Set` of listeners rather than a React context,
because there is one consumer and it is `App`. `getToken` also checks the stored
expiry locally, as a courtesy: an expired session shows the login form instead
of a table that fails to load. The API checks the same expiry for real.

### How it gets attached

`src/api/client.js` exports one configured axios instance with two interceptors:

- **Request** — reads the token from the store and sets `Authorization: Bearer`.
  Read per request, not captured at startup: the token arrives after the module
  is first evaluated, and it can be cleared while the app is running.
- **Response** — a 401 clears the session, which notifies `App`, which renders
  the login form. Without it a spent token would fail every request on the page
  in the same way, forever.

`authApi.js` deliberately uses **bare axios** rather than that instance. Sending
a stale token to the endpoint that issues tokens is pointless, and the 401
interceptor would sign you out for mistyping your password.

Signing out just calls `clearSession()`. There is nothing to tell the API: the
token it issued is stateless and stays valid until it expires, so forgetting it
here is what ends the session as far as this browser is concerned.

## API Layer

| Module | What it does |
|---|---|
| `api/client.js` | The axios instance, the base URL, and both interceptors |
| `api/session.js` | The token store — `localStorage` plus subscribe/notify |
| `api/authApi.js` | `login` / `register`, and the demo credentials |
| `api/billApi.js` | The five CRUD calls, all through `client.js` |

`billApi.js` provides all CRUD operations:
```
getBills({ page, pageSize, search, status, sort, dir })  
getBill(id)  
createBill(bill)  
updateBill(id, bill)  
deleteBill(id)
```

None of them take an owner. Bills are scoped server-side by an EF Core global
query filter, so an unauthenticated `getBills()` does not return everyone's
bills — it returns 401, and a `getBill(id)` for somebody else's bill returns
**404**, not 403.

## Paging

The list endpoint pages, so `getBills` asks for one page and the response is an
object rather than an array:

```json
{ "items": [ ... ], "page": 1, "pageSize": 10, "totalCount": 26,
  "totalPages": 3, "firstRowNumber": 1, "lastRowNumber": 10,
  "hasPrevious": false, "hasNext": true }
```

Two consequences for this app:

- **Search is a server round trip.** It used to be `bills.filter(...)` over the
  whole table; against a paged endpoint that would have searched the ten rows
  on screen. Typing is debounced by 300 ms and always returns to page 1.
- **The counts come from the response, not from `items.length`.** "Showing 1-10
  of 26" is a claim about the database, and one page cannot make it.

See the [API README](../../BillsMinimalApi/README.md#get--a-page-of-bills) for
the full parameter list.

## How the Frontend Works

### 1. How do you call a REST API when the page initializes?
```
useEffect(() => {
  let current = true;

  async function load() {
    const response = await getBills({ page, pageSize: PAGE_SIZE, search: appliedSearch });
    if (!current) return;          // a slow page 2 must not overwrite a fast page 3
    setBills(response.data.items);
    setPageInfo(response.data);
  }

  load();
  return () => { current = false; };
}, [page, appliedSearch, reloadToken]);
```

### 2. What code fetches and displays all items?
```
<tbody>
  {bills.map(b => (
    <tr key={b.id}>
      <td>{b.id}</td>
      <td>{b.payeeName}</td>
      <td>{b.dueDate.slice(0,10)}</td>
      <td>{b.paymentDue}</td>
      <td>{b.paid ? "Yes" : "No"}</td>
    </tr>
  ))}
</tbody>
```

### 3. How is the new-item form rendered and submitted?
```
<input value={newBill.payeeName}
       onChange={e => setNewBill({...newBill, payeeName: e.target.value})} />

async function handleCreateBill(e) {
  e.preventDefault();
  await createBill(newBill);
  loadBills();
}
```

### 4. How do you find and update an existing item?
```
<button onClick={() => setEditRowId(bill.id)}>Edit</button>

<input value={bill.payeeName}
       onChange={e => handleRowChange(bill.id, "payeeName", e.target.value)} />

async function handleSaveRow(bill) {
  await updateBill(bill.id, bill);
  loadBills();
}
```

### 5. How do you delete an item?

```
async function handleDelete(id) {
  await deleteBill(id);
  loadBills();
}
```

This frontend provides simple CRUD operations with a clean React + Axios structure.
