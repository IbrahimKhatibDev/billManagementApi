# Follow-up: mobile layout for the redesign

The Bills Manager redesign is designed for ≥1240px. The design handoff
(`design_handoff_bills_manager_redesign/README.md`) says so in as many words,
and asks that no mobile treatment be guessed at while building the desktop pass.
This is that flag.

## What exists today below 1240px

The shell works. The sidebar becomes a drawer under 641px with a backdrop and a
top bar, exactly as it did before the redesign — re-skinned in tokens, not
redrawn. That much was re-tested during the desktop pass: the drawer opens and
closes from both the top-bar button and the backdrop, and the page behind it is
clickable again once it closes.

**The screens themselves do not reflow.** This is worse than "not designed", and
the distinction matters to whoever picks this up: outside the shell, the app has
no responsive rules at all. `MainLayout.razor.css` and `NavMenu.razor.css` hold
the only three media queries in the application. Every page component — Bills,
BillGroup, Overview, Reports, PaidRateStrip, PayeePareto, CashFlowTimeline —
ships a desktop grid and nothing else, so at phone widths the grids keep their
desktop column counts and their contents overflow the viewport.

`.content` carries `overflow-x: hidden`, so this does not produce a horizontal
document scrollbar. It produces something less obvious and more serious: the
overflow is **clipped**, and no ancestor scrolls, so anything past the right edge
cannot be reached at all.

Measured at a 375px viewport:

- **Bills** — roughly 110 elements extend past the right edge. The per-row
  delete button lands at x=528–561 and `document.elementFromPoint` at its centre
  returns `null`. The status toggle is likewise out of reach. Nothing in the
  ancestor chain scrolls horizontally, so there is no gesture that brings them
  back. **Rows cannot be deleted or marked paid from a phone.**
- **Overview** — the "Mark paid" buttons on the late list reach x=472–495 and
  are clipped the same way.
- **Reports** — the four headline figures hold a four-column grid at 69.75px a
  column, so a value like `$1,235.59` needs 130px and spills out of its own
  cell.

At 900px all three screens are clean: no overflow anywhere, and the sidebar is
still a sidebar.

None of this is a regression from the shell re-skin. The phone layouts that used
to live in `site.css` targeted `.bills-table` and `.report-table`, markup the
Bills and Reports rebuilds had already deleted; they were dead rules before they
were removed. The gap arrived with the rebuilt screens, which were built to the
desktop spec the handoff scopes them to.

Beyond reachability, several things are merely tolerable:

- **The paid-rate strip** divides its width by the number of months. At a
  twelve-month range on a phone that is roughly 25px a cell, which is a row of
  slivers rather than a chart. It already trims to 24 months and says so; on a
  phone the useful number is nearer six.
- **The weekly cash-flow timeline** is a fixed-height SVG scaled to the
  container. At phone widths the week ticks overlap.
- **The Pareto table** has four columns, one of which is a bar. On a phone the
  bar column is the first thing that should go.
- **Inline editing** puts a text input in a cell sized for desktop.

## What a mobile pass has to decide

0. Whether reachability is fixed ahead of the rest. It is the one item here that
   is a functional defect rather than an undesigned layout, and it does not need
   the mobile design to be settled — a horizontal scroller on the row, or moving
   the row actions somewhere that survives a narrow viewport, restores access
   without committing to a phone treatment.
1. Whether the bill groups stay tables or become cards, as the old bills table
   did below 768px. Cards cost the alignment that makes a column of money
   readable; tables cost horizontal space there is none of.
2. What the timeline becomes. A shorter window (four weeks rather than the whole
   book) is a different chart, not a smaller one.
3. Whether the paid-rate strip windows to the last six months, scrolls
   horizontally, or is dropped from the phone layout.
4. Where the theme toggles live when the nav is a drawer that is closed by
   default.
5. Whether bulk selection survives. The sticky action bar works at any width;
   a per-row checkbox column is what does not.

## What is already true and should not be redone

- Every colour is a token, so the four themes work at any width already. This
  was measured across all four palette × mode combinations, not eyeballed.
- The drawer, the backdrop and their z-index ladder are correct and were
  re-tested during the desktop pass.
- No layout below 1240px is load-bearing for any test.
