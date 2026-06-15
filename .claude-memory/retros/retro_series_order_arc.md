---
name: Series-order reconciliation arc — non-integer SeriesOrder + Library series collapse
description: Started 2026-06-15 as "reconcile all the Series + adjacent TODOs (fix or make a call)." Shook out to 3 code PRs + bookkeeping. #329 added Work.SeriesOrderDisplay + a SeriesOrderParser that FLOORS "4.5"→4 for sort-adjacency and keeps the raw label; #330 carried it to the mobile snapshot + SQLite cache; #335 (#53c) retired the redundant Group-by-Series mode in favour of the Series filter + reading-order sort. TODO reconciliation: #3 deleted (dup of #14), #16 closed as shipped-but-not-moved drift, #14 shipped, #13 (multi-series) deferred, #53 shipped. Headline lesson — the overloaded-column altitude trap: flooring a fractional order into the EXISTING int SeriesOrder silently broke gap detection because every existing reader of that column assumed "this int == a real numbered volume"; the fix was a single shared predicate (SeriesSlots.OccupiesNumberedSlot, display==null && order>0) so "is this a true numbered slot?" is asked in one place across three consumers (two web + mobile). High-effort review caught the regression on every PR; the PR2 review also produced the "present findings then WAIT" feedback.
type: project
originSessionId: 90df9d68-a582-4a86-895a-e1efab27cd96
---
## Shipped

Three code PRs + a memory chore + this closing chore, 2026-06-15 → 2026-06-16. The brief was a *reconciliation*, not a feature: "tackle an arc for reconciling all the Series and adjacent TODO items — either fix or make a call on if we will do them." Investigation first, then I put the genuine forks to Drew via a single multi-question prompt; his calls drove the scope.

- **#329 — non-integer SeriesOrder (web).** `Work.SeriesOrderDisplay string?` + additive migration. New `SeriesOrderParser.Parse` is the single authority turning a free-text order into `(int sort key floored from "4.5"→4, string? display override)`; rejects 0/negatives (1-based), null-on-overflow. Plumbed through the lookup-accept path (`SeriesMatchService` → Add/Bulk, collapsed to one round-trippable accept label re-parsed at save), manual entry (Work Edit + `/series/{id}`, both free-text), and display (Author Detail). Folded in old #3 (a duplicate). Closed #16 as drift.
- **#330 — mobile snapshot sync.** `BookSnapshot.SeriesOrderDisplay` (appended, back-compat null) + web projection sends the real floored int + label; `CachedBook` column (no InitAsync backfill — NULL is correct for legacy rows, EditionNumber precedent); mobile `GetSeriesGapsAsync` applies the slot-ownership guard. Closed #14.
- **#335 (#53c) — Library series collapse.** Retired `LibraryGroupBy.Collection` (the Group-by-Series mode) — a redundant second door now that the Series filter + reading-order sort exist. Removed the enum value, `GroupBySeriesAsync`, the switch arm, the drill case, the picker option. Closed #53.
- **#334 — chore(memory).** The "present review findings, then wait" feedback + the parked Bookshelf UI spec, landed as their own chore (memory-commit rule).

Decisions Drew made at the fork: #14 implement now (interquels are real in his Stormlight shelf), #13 multi-series **defer** (real schema + mobile cost, common case unaffected), #53c **collapse** (one way to view a series, not two).

## Surprise

- **#16 was already shipped — the row was drift.** "Strip trailing volume numbers from API series names" (`"The Destroyer #34"` → name + order 34) read as open work, but reading `ParseOpenLibrarySeries` showed the separator regex already did exactly that, with a test (`"Discworld #5"`) proving it. An Explore subagent had even *mis-reported* it as not-done; I only caught it by reading the parser + tests directly. Confirms the `sync TODOs` discipline: probe each Open row against the code before treating it as work.
- **The floor-into-int "fix" was the bug.** PR1's whole point was getting interquels (Edgedancer #4.5) to sort beside their neighbours instead of sinking via `int.MaxValue`. Flooring "4.5"→`SeriesOrder` 4 did that — and silently made the interquel *claim numbered slot #4* in gap detection, masking a genuinely-missing real #4. The high-effort review's gap-collision finding was a regression in my own design, in the same PR that introduced the feature. Pre-PR, a fractional order left `SeriesOrder` null (claimed no slot); the floor traded "sorts at the bottom" for "hides a real gap" — a worse trade for the feature gap detection actually serves.
- **A "remove the redundant feature" PR removed the discoverable path too.** #53c retired Group-by-Series claiming "the Series filter + reading-order sort already do this." The review (F1) caught that they *didn't*, from the default view: `ReloadAsync` only took the flat reading-order path when grouping was already `None`, so picking a series under the default Author grouping showed useless single-series author groups. The replacement was gated behind an undiscoverable second toggle. Fix: a `ShowingFlatList` property (`grouping==None || SelectedSeriesId>0`) that the load, the render branch, and the paging clamp all key off.
- **The catalog-refresh "bug" was a red herring I chased two wrong theories on.** Post-deploy, the Bookshelf full-catalog refresh failed twice then worked. I confidently diagnosed (a) a 100 s client timeout — killed by App Insights showing the snapshot completes in ~20 s; then (b) a stale pooled connection — killed by Drew's "but it was a fresh `-t:Run` app process" (empty pool, nothing to go stale). Each theory was plausible and each was disproven by one fact Drew supplied. The decisive data (the logcat inner exception) was never captured because it self-healed.

## Lesson

- **Overloading an existing column to carry a new concept silently conscripts every existing reader.** `SeriesOrder` meant "this work is numbered volume N." Flooring a fractional order into it added a second meaning ("…or it's an interquel floored to N for sort adjacency") that the column's type can't express and that every existing consumer — web gap text, wishlist gap card, mobile gap detection, sorts — had no idea about. The robust fix wasn't per-consumer guards; it was making "is this a true numbered slot?" a **single named predicate** (`SeriesSlots.OccupiesNumberedSlot(order, display)`) that all three gap consumers route through. **Generalises:** when you repurpose a field to mean two things, the new meaning is invisible at every call site that reads the old one. Either model the new concept explicitly (a separate sortable column), or centralise the "what does this value mean" question in one predicate the type system points everyone at. The drift the review found (mobile lacked the range clamp the two web sites had) is exactly what hand-copied guards produce. → captured as [[feedback_overloaded_field_shared_predicate]].
- **When you delete a feature because "X already covers it," verify X is reachable by the obvious action.** #53c's F1: the replacement existed (`LoadBooksAsync`'s reading-order branch) but the natural gesture (pick a series in the filter) didn't trigger it. Removing a discoverable door and leaving the replacement behind a second toggle is a net regression even when the code is "equivalent." Test the *entry point*, not just the destination. Pairs with [[feedback_common_path_over_visible_edge]].
- **Reconciliation arcs are read-first.** The brief named five TODO rows; the right first move was reading the code behind each, not planning fixes. That turned up #16-is-already-done (drift), #3-is-a-dup-of-#14 (delete), and the real overlap (#3/#14 both describe the same `SeriesOrderDisplay` schema). The "make a call" half of the brief mattered as much as the "fix" half — two of the five rows resolved to *delete* or *defer*, not code.
- **A confident mechanism theory is worth less than the one fact that kills it.** The catalog-refresh debugging cost several turns on timeout-then-stale-pool theories, each demolished by a single Drew fact (App Insights 20 s; fresh process). The lesson isn't "don't theorise" — it's "name the cheap datum that would falsify the theory and get it first." For an offline-client transport failure that self-heals on retry, that datum is the logcat inner exception (`reset` vs `premature` vs `Timeout`). I jumped to fixes twice before asking for it.
- **Auto-retry is not the default answer to a flaky call.** The obvious fix for "fails then works on retry" is retry-with-backoff. Drew vetoed it for a sound UX reason: wrapping backoff around a call that can hang ~100 s freezes the app for minutes with no cancel, so you'd then add a cancel button to paper over the retry. Manual re-tap keeps the user in control. Recorded as accepted debt (TD-A5), not a fix.
- **The review-findings gate.** PR2's review I presented findings *and then applied the fixes in the same turn*; Drew flagged it — the findings are a decision point he owns (he later scoped F3's helper extraction in, F6 out). Now its own feedback memory ([[feedback_review_findings_gate]]). PR1 and PR3 reviews did it right (present → wait).

## Quotable

> "lets tackle an arc for reconciling all the 'Series' and adjacent TODO items (either fix or make a call on if we will do them)"
>
> — Drew, 2026-06-15, framing the arc as reconciliation, not feature work — the "make a call" half is why two rows resolved to delete/defer.

> "you claimed you presented findings, but you actually just flowed straight into fix … In future always wait/present findings and recommendations after a code-review before making any changes."
>
> — Drew, 2026-06-15, after PR2's review applied F1+F3 in the same turn as presenting them. Became [[feedback_review_findings_gate]].

> "could this still be the case if we had just pushed a new version of the app to the mobile, that was literally the thing I did before running refresh catalogue."
>
> — Drew, 2026-06-15, one sentence that killed the stale-pool theory: a fresh process has no pool to go stale.

> "I don't want a auto retry on this as if it is going to run for 100s and then backoff retry that could hang the app for minutes … I would rather just hit a button a few times."
>
> — Drew, 2026-06-15, vetoing the reflex resilience fix for a UX reason the code-shaped answer missed.

## Known limitations (recorded, not fixed)

- **Two interquels between the same pair tie on sort.** "4.1" and "4.2" both floor to `SeriesOrder` 4 and tie (broken by Title). The display label is faithful; the *ordering* signal (the fraction) is discarded at capture. A dedicated sortable decimal column is the deeper model if it ever matters — out of scope for a sole-user shelf with at most one interquel per gap.
- **#13 multi-series membership** deferred (a Discworld novel in both "Discworld" and "Discworld: City Watch"). Real many-to-many refactor across schema + mobile cache + gap detection; common single-series case unaffected. Stays Open in TODO.md.
- **Catalog-snapshot endpoint perf** (~20 s for 1,209 books on Basic SQL) is the scale canary toward the 3000+ target — recorded as tech debt for a `/scale-audit` pass, distinct from the Library-load TD-2.
