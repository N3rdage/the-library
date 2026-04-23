---
name: Book View page — Library swap + arc wrap-up
description: Two-line PR that flipped /books list clicks from /edit to the new View page, capping a five-PR arc. The swap was trivial because the preceding four PRs made it the obvious default. Also an arc-level retrospective on the rollout shape — scaffold → feature → feature → feature → default — as a way to introduce new UX surfaces without a high-stakes cutover.
type: project
---

## Shipped

PR #100 — flipped the Library list click target from `/books/{id}/edit` to `/books/{id}` (the View page). Two lines of actual code: rename the `NavigateToEdit` method, change its URL. Plus the temporary "View (preview)" link on the Edit page (added in PR 1 as a throwaway way to reach the new page without the rest of the app depending on it) became a permanent "Back to view" link — symmetric counterpart to View's "Full edit page" escape hatch.

## Surprise

- **The swap was a non-event, and that's exactly what the opt-in rollout was designed to deliver.** Four PRs of building (scaffold → inline saves → modal edits for Book+Work → modal edits for Edition+Copy) and the swap is 2 lines of `.razor`. No data migration, no schema change, no coordinated deploy, no feature flag. That's the *payoff* of having shipped PR 1 as "reachable only via a preview link" — the View page accrued features in the open, against real data, without the whole app depending on it until it was feature-complete for the common case.
- **PR #100 as a milestone is a coincidence but pleasing.** Five-PR arc ended on the round number. The codebase crossed the 100-PR mark with the book-detail work done.

## Lesson

- **"Scaffold opt-in, swap when feature-complete" is the template for introducing a new UX surface.** The shape:
  1. **PR 1 — scaffold.** New surface lives alongside the old one. Reachable only via a temporary/preview link so existing navigation isn't disturbed.
  2. **PRs 2–N — feature parity.** Land the features that make the new surface good enough to be the default. Each PR independently reviewable, no "half-done" limbo.
  3. **Swap PR — flip the default.** Typically 1–2 lines. Cheap to revert if something's wrong, cheap to ship when everything's right.
  4. **Decommission PR (optional, later) — retire the old surface.** Only once you're sure nothing depends on it.
  Feature flags are the heavier variant of this shape for teams that can't afford to route 100% of users through a half-baked surface. For a solo-dev personal project, the preview-link-plus-swap is the right granularity — no infrastructure to set up, same safety property (you can ship PR 1 without committing to PR 100).
- **The "Full edit page" / "Back to view" symmetry is load-bearing UX.** Users arriving on View via the Library can reach `/edit` for genres and compendium building; users arriving on `/edit` (via bookmarks or other flows that still point there) can step back to View. Navigating between the two is explicit and reversible. No trapped-on-a-page dead end. Worth building in deliberately; easy to forget when you're swapping one direction of the flow and not the other.
- **The other `/edit` links left behind are a deliberate scope boundary.** Shopping page "View book" link (mislabeled — actually goes to /edit), Series Edit badge links, Duplicates flow links, Add-after-save navigate — all still point at `/edit`. Each is a separate UX decision that deserves its own thinking. The Library swap was correct in isolation; bundling every other link into the same PR would have blurred the review and slowed the merge. Small PRs are not about being small for its own sake; they're about keeping each decision evaluatable.

## Arc wrap-up — Book View page, PRs #95–#100

Five PRs, one coherent feature:
- **PR #95 (scaffold)** — read-only View page, reachable only via preview link. Single-Work collapse vs multi-Work searchable list. The UX bet: "does the mental model work?"
- **PR #97 (inline saves)** — rating, status, notes, tags editable without leaving the page. Durable UX rule: silent saves when the value IS the feedback; visible save-state when the user composed the value.
- **PR #98 (modal edits — Book + Work)** — first modals, per-modal VMs. Author typeahead. MudListItem stopPropagation gotcha.
- **PR #99 (modal edits — Edition + Copy)** — dual-mode dialog VMs (Add+Edit via flag). Inline delete-confirm. Caught the EF InMemory vs SQL Server case-sensitivity footgun — arguably the highest-value lesson of the arc.
- **PR #100 (swap)** — 2 lines, flipped the default.

**What the arc validated:**
- Opt-in rollout with a preview link scales to a 5-PR arc without pain.
- Per-modal VMs (rather than stuffing everything on the page VM) kept the page VM at ~280 lines across the arc. Without the discipline, it would have been 600+.
- MudBlazor's `MudAutocomplete` + `SearchFunc` + `CoerceValue` is the right primitive for find-or-create typeaheads (used for Author, Publisher, Tag within the arc).
- "Full edit page" as a named escape hatch means we can ship features that don't cover 100% of the edit surface — genres can stay hierarchical on `/edit`, compendium building can stay on `/edit`, and users who need those still know where to go.

**What's still on `/edit` after the arc:**
- Hierarchical genre picker (MudBlazor rebuild is a dedicated future PR — it's a real UX component, not a form field).
- Compendium building ("Add other works" / "Attach existing Work" typeahead).
- "Edit" deep-links from Shopping, Series, Duplicates, Add-after-save — not yet reconsidered.

**What a book-detail follow-up session might look like:**
- Rebuild genre picker in MudBlazor + add to the WorkEditDialog.
- Move compendium-building flows (add/attach Work) into a modal on View.
- Pass through the other `/edit` link sites and decide which should also land on View.
- Retire `/edit` entirely, eventually. But don't rush it — it costs nothing to keep.

## Quotable

The four feature PRs each landed against real data, reviewed in isolation, shippable on their own. The swap was 2 lines. If you count the work as "2 lines to land a feature", it looks trivial; if you count it as "5 PRs over a week", it looks heavy. Both are true. The value of the preview-link-plus-swap rollout is that you get the 5-PR safety property with the 2-line deployment risk. You don't buy that trade-off with feature flags or infrastructure; you buy it with a convention — "new surface ships as opt-in, swap only when feature-complete" — that a solo dev can hold in their head. The smaller the project, the more valuable the conventions that don't need tooling to enforce them.
