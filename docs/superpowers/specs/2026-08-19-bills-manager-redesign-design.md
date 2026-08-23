# Bills Manager redesign — design

**Date:** 2026-08-19
**Source:** `design_handoff_bills_manager_redesign` (Claude Design handoff — README, two prototype HTML files, nine reference screenshots)
**Build target:** `bills-frontend/BillsFrontEndBlazor` (confirmed; the handoff asks for this to be confirmed before starting)

## Goal

Rebuild the three Blazor screens — Overview, Bills, Reports — to the redesign, including all ten UX
changes, the four palette/mode combinations, Phosphor icons, and the backend surface the redesign
needs but the API does not yet have.

## Decisions taken before design

| Question | Answer | Consequence |
|---|---|---|
| Blazor or React? | Blazor | All three redesigned screens already exist. React has only sign-in and a bills table. |
| How much of the handoff? | All ten ideas + the new parse endpoint | Idea 8 is inert without server-side parsing. |
| Below 1240px? | Keep working, undesigned | No regression from today's drawer/rail. Designed mobile is a flagged follow-up. |
| Approach? | Token layer + component extraction, in place | Keeps Bootstrap 5 per the handoff; avoids a parallel page set and a big-bang cutover. |

## Architecture

### Token layer

All colour, radius, and type values become CSS custom properties declared in four blocks on the root
element, selected by two independent attributes:

```
<html data-palette="nocturne|current" data-mode="light|dark">
```

Four blocks, not a single light/dark switch — the handoff is explicit that Nocturne and Current are
separate brand directions each with their own light and dark variant.

Token values are taken verbatim from the handoff README:

**Nocturne dark** — bg `#161826`, surface `#232532`, sunken `#1b1d2a`, text `#e9e9ed`, muted
`#9397ab`, faint `#75798c`, accent `#9184d9` (outline only, never a flood fill), accent-text
`#d2cefd`, late `#b5abfc`, ok `#75798c`. Aging ramp `#595d6c #5d5294 #796cbf #968ae0 #b5abfc`.

**Nocturne light** — bg `#f4f3f9`, surface `#ffffff`, sunken `#ece9f7`, text `#1e1c2e`, muted
`#6b6880`, accent `#7c6dd1`, accent-text `#5b4bc4`. Aging ramp `#d9d5ec #b8b0dd #9184d9 #6a5cc2
#4a3a9e`.

**Current light** — bg `#f8f9fa`, surface `#ffffff`, accent `#0d6efd`, late `#dc3545`, ok `#198754`.
Aging ramp `#adb5bd #ffc107 #fd7e14 #dc3545 #842029`.

**Current dark** — bg `#17191c`, surface `#1f2226`, accent `#3d8bfd`, late `#ea868f`, ok `#75b798`.

Shared across palettes: radius 8px / 14px, `1px solid` border as the only elevation cue (no drop
shadows), Inter for Nocturne and Helvetica Neue/Arial for Current.

`data-mode` is mirrored onto Bootstrap 5.3's own `data-bs-theme` so Bootstrap components (modal,
dropdown, form controls) follow the mode without being restyled individually.

### Applying the theme without a flash

`_Layout.cshtml` gets a small inline `<script>` in `<head>` that reads `localStorage` and sets both
attributes **before first paint**. This is not optional polish: `IJSRuntime` is unusable during
`ServerPrerendered`'s first render — a constraint already documented in `Index.razor` — so a
Blazor-side read would necessarily run after the page has painted, flashing the wrong theme on
every load.

`ThemeSwitcher.razor` writes the choice to `localStorage` and sets the attributes via JS interop
from `OnAfterRenderAsync`, where interop is available.

### Charts

The inline SVG charts render from C# with no JS interop and no network fetch — an existing
deliberate constraint. They currently hardcode fills (`fill="#212529"`, `fill="#6c757d"` in
`Index.razor`). Those move to `var(--token)` references so charts re-theme with everything else.

## Backend changes

Four changes. The handoff names one; three more were found by reading `BillSummary` against the
screenshots.

### 1. `POST /bills/parse` (idea 8)

```
Body:    { "text": "Verizon 89.20 fri" }
Returns: { "payee": "Verizon", "amount": 89.20, "dueDate": "2026-08-21", "confidence": "high" | "low" }
```

Regex and date-library based, not NLP:

- **payee** — free text up to the first number
- **amount** — the first `\d+(\.\d{2})?` token
- **date** — the last token, resolved against a weekday/relative grammar: `today`, `tomorrow`,
  weekday names and three-letter abbreviations (resolving forward to the next such day), `8/21`,
  `aug 21`
- **confidence** — `high` when all three parts resolved, `low` otherwise

Returns the pieces **uncommitted**. The client renders the reading for the user to confirm or
correct before POSTing a real bill. Parsing logic lives in a plain class so it is unit-testable
without a web host.

### 2. `Weeks` on `BillSummary` (idea 2)

The cash-flow timeline is weekly; `Months` is monthly. Add a list of week buckets, each carrying
week-start date, paid amount, and unpaid amount.

Like every other aggregate on `BillSummary`, `Weeks` describes the requested window. "Every bill on
the books" is achieved by Overview requesting an **unbounded** window (`from` and `to` both null),
not by `Weeks` ignoring the range it was asked for — an aggregate that quietly disregarded the
window would contradict the rest of the response.

### 3. `Late` on `BillSummary` (idea 3)

`Priority` is capped at six by `PriorityCount` and is deliberately "a shortlist, not a second bills
page". The Overview triage list shows every late bill (eight in the reference data). Add a full
late list, oldest due first.

### 4. `OldestDaysLate` on `BillSummary` (idea 1)

The obligation sentence says "the oldest by 156 days". Nothing currently computes it.

All three aggregates go in `Queries/BillSummaryBuilder.cs` alongside the existing ones. Computing
them client-side would break the single-consistent-window guarantee that `BillSummary`'s own
documentation argues for — every section of the response must describe the same window, computed at
the same instant.

**Already correct, no change needed:** the API's aging bucket labels (`Not yet due`, `1–30 days
late`, `31–60 days late`, `61–90 days late`, `Over 90 days late`) match the design exactly.

## Components

New, in `Shared/`. Each has one job, renders from parameters, and is understandable without reading
its consumers.

| Component | Idea | Job |
|---|---|---|
| `ThemeSwitcher` | — | The two toggles; persists to `localStorage` |
| `Icon` | — | Phosphor wrapper; one place that knows the class convention |
| `ObligationHeadline` | 1 | Total owed, how much late, how old the worst |
| `CashFlowTimeline` | 2 | Weekly stacked paid/unpaid bars with a "now" marker |
| `LateBillsList` | 3 | Late bills oldest first, one-click mark-paid |
| `AgingStrip` | 4 | Five buckets as one stacked strip plus legend |
| `BillGroup` | 5 | One due-window section with its count and sum |
| `BulkActionBar` | 6 | Sticky bar reporting selected count and total |
| `InlineEdit` | 7 | Click-to-edit a date or amount in place |
| `QuickAddBill` | 8 | Free-text input with a correctable parse preview |
| `PayeePareto` | 9 | Payees ranked by outstanding with running cumulative % |
| `PaidRateStrip` | 10 | Monthly paid rate as shaded cells |

`AnimatedCounter` already exists and is reused for the Reports headline count-up (900ms ease-out on
mount and on range change) rather than reimplemented.

Extraction is not incidental: `Bills.razor` is already 401 lines with a 679-line code-behind, and
`Reports.razor` 475 with 406. Adding ten structural changes inline would make them unreadable and
unreliable to edit.

## Screen composition

### Overview

Replaces three counter cards, the marketing hero, the donut, the six-month bar chart, and the three
quick-action tiles.

- `ObligationHeadline` — "WHAT YOU OWE", the total, then the sentence: *"$1,398.99 of it is already
  late, spread across 8 bills — the oldest by 156 days. The rest, $289.99, falls due inside the next
  30 days."* Two buttons: "Clear the N late bills" navigates to Bills with the Overdue filter
  applied — it does **not** bulk-mark anything paid, per the prototype's handler — and "All bills"
  navigates to Bills unfiltered. Both use the deep-link pattern `Index.razor` already uses for
  `bills?new=true`.
- `AgingStrip` — "How late it is" / "Every unpaid bill, by age."
- `CashFlowTimeline` — "Cash-flow timeline" / "Every bill on the books, by the week it falls due",
  legend `unpaid` / `paid`, `now` marked on the axis.
- `LateBillsList` — "Late — oldest first" / "The only thing on this page that needs doing today.",
  headed by "N bills · $X".

### Bills

- `QuickAddBill` — placeholder *Add a bill in words — try "Verizon 89.20 fri"*, beside an "Add bill"
  button that opens the modal.
- Filter chips All / Unpaid / Overdue / Paid, payee search, and a right-aligned "N of M bills · $X".
- Five `BillGroup` sections replacing pagination, in order: **Late**, **Due this week**, **Due this
  month**, **Later**, **Paid**. Group predicates taken from the prototype. Empty groups are hidden.

  Filters and grouping compose in one direction: the chips and the search narrow the set, then the
  groups partition what survives. So Overdue shows a single Late group, and Paid a single Paid
  group. This is what makes the Overview's "Clear the N late bills" deep-link land on exactly the
  eight bills the sentence counted.
- Row checkboxes feeding `BulkActionBar`.
- `InlineEdit` on dates and amounts. The modal survives only for creating a bill from scratch —
  footer copy: *"Dates and amounts are editable where they sit — click one. The modal survives only
  for a bill you are creating from scratch. Open the create form"*.

**Paging.** Idea 5 spans the whole book, so the page can no longer fetch one page at a time. It uses
the same full walk `GetAllInRangeAsync` already performs for CSV export, with a hard cap of 500 rows
and an explicit "showing the first 500 of N" notice when exceeded — a silent truncation would read
as a complete book when it isn't.

### Reports

- Range presets All time / This year / Last 6 months / Last 3 months / Next 3 months, a description
  line, and Export CSV — all kept.
- Four headline cards (Total billed, Paid, Outstanding, Overdue) with sublines, counting up via
  `AnimatedCounter`.
- The typical/mean/largest line and the size-band sentence.
- `PaidRateStrip` — "Paid rate by month" / "Share of each month's money that has actually been paid."
- `PayeePareto` — "Who you owe" / "Ranked by outstanding, with the running share of the total.",
  with the "Three payees account for 62% of everything you owe." framing. Columns: Payee,
  Outstanding, Running share, Cum %.

## Icons

Phosphor regular, 16–20px inline with text. Vendored into `wwwroot/css/phosphor/` — matching the
deliberate existing choice to self-host Bootstrap and Bootstrap Icons rather than pull from jsdelivr,
so Docker builds stay hermetic. Every `bi-*` class gets a mapped `ph-*` replacement; Bootstrap Icons
is removed once nothing references it.

## Data flow

Unchanged in shape. `BillService` remains the only thing that talks to the API. Overview and Reports
both read one `BillSummary` for their window; Bills reads rows plus a count. The new aggregates ride
on the existing `GET /summary` response rather than adding round trips, preserving the
one-response-per-window property.

`BillSummary.AsOf` — the server's idea of today, already sent — drives the "as of Aug 18, 2026"
subtitle on every screen, so the client never renders its own clock's answer.

## Error handling

Existing behaviour is kept, not redesigned:

- `BillWriteResult` already distinguishes 409 (lost race), 404 (gone), 400 (rejected), and 401
  (session expired), each with its own message. Bulk mark-paid and inline edit both surface these
  per-row rather than collapsing to one generic failure.
- A partially-failed bulk operation reports how many succeeded and how many did not.
- API unreachable stays a failed result, never a rethrow — an unhandled exception tears down the
  Blazor Server circuit and replaces the page with the yellow error bar.
- `POST /bills/parse` returning `confidence: "low"` is not an error. The preview renders with the
  uncertain fields marked for correction.

## Testing

- **Unit** (`BillsMinimalApi.UnitTests`): the parsing grammar, week bucketing, cumulative-percent
  arithmetic, and due-window group predicates — all plain classes, no web host.
- **Integration** (`BillsMinimalApi.Tests`, Postgres fixture): `POST /bills/parse` behaviour and the
  three new `BillSummary` aggregates, in the style of the existing `BillSummaryTests.cs`.
- **Not this pass:** bUnit for component tests. Adding a component test framework is a new dependency
  and a separate decision; the logic worth testing is being deliberately kept out of the components.

## Out of scope

- **Designed mobile layout.** Below 1240px the re-skinned drawer and rail keep working and sections
  stack, but this is undesigned. The handoff says not to guess a mobile treatment; this is the
  follow-up it asks to be flagged.
- **The "Ideas" toggle and the numbered 1–10 markers.** Handoff scaffolding for reading the
  prototype, not product.
- **The React frontend.** Untouched. The parse endpoint is built as shared API surface it could
  adopt later.
- **Seed data.** The app reads its live API. The handoff's exact figures are reference values for
  checking the build against the screenshots, not fixtures to install.
