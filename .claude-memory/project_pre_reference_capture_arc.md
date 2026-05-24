---
name: pre-reference-capture-arc
description: "Ordered work queue between PR 2d (WorkAuthor Role cutover Phase F+G+H, merged 2026-05-22 as"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8711e460-3ba4-406f-a87f-f9c80a403039
---

Drew's plan after PR 2d landed: do the "useful" things before reference-book capture kicks off, since fiction capture is winding down and reference is the next big wave. He explicitly named the ordering 2026-05-22.

**Why:** Reference books surface the gaps the new Role model was built for — editors of Oxford Companions, translators of classics, illustrators of field guides. Best to bed in the surrounding scaffolding *before* mass-capture rather than retrofit afterwards (TODO #51 already calls out that backfilling editor-vs-author credit on already-captured rows is sunk-cost work).

**How to apply:** When Drew next opens a "what's next" conversation, propose this order rather than jumping back to whatever's at the top of TODO.md. Items below pair naturally — a single planning session can scope all three buckets if Drew wants them bundled.

## Bucket 1 — Lookup-side contributor detection (TODO #52) — **DEFERRED 2026-05-24**

The MudContributorPicker shipped in PR 2d is empty on every API hit because `BookLookupResult` only carries a flat `Author` string. Open Library has role data in `authors[].type` and `contributors[].role`; Google + Trove don't.

Drew's call 2026-05-24: skip the parser, revisit only if hand-typing roles becomes annoying during reference capture. The conversation instead surfaced a more pressing problem — Author was mandatory on the Add Book flow, forcing "Unknown" entries for editor-only books (dictionaries, Oxford Companions, many cookbooks). That **shipped same session** as a small PR: `AuthorResolver.AssignAuthors` + the four save paths (`BookAddViewModel`, `BookDetailViewModel.CreateAndAttachWorkAsync`, `WorkEditDialogViewModel.SaveAsync`) now require "at least one contributor of any role" instead of "at least one Author"; dialogs + Snackbar + form captions updated; `CatalogSnapshotService` falls back to lowest-Order non-Author with role suffix via new `WorkAuthorshipFormatter.DisplayPrimary` helper. Bulk Add stays Author-only per the Phase F call. See `feat(workauthor): allow editor-only Works (relax mandatory Author)` commit on branch `feat/editor-only-works`.

The original parser plan (coverage-sampling pass shape from the 2026-05-08 TOC investigation, then DTO + OL parser + Add Book pre-populate + suggestion banner) stays in TODO #52 with a deferral note for if/when it's reopened.

## Bucket 2 — WorkAuthor cutover Phases D + E (Mobile) — **SHIPPED 2026-05-24**

Single PR (`feat/mobile-contributor-roles`). Phase D added `CachedBookWork.ContributorsJson` + write paths in `PopulateAsync` / `ApplyDeltaAsync` / `GetBookEnrichedDetailAsync`, paired with the [[sqlite-net-pcl-schema-backfill]] `UPDATE ... WHERE ContributorsJson IS NULL OR ''` in `InitAsync` so existing rows survive the deserialise. Phase E added `BookTracker.Mobile.Cache.ContributorFormatter.Format` (mirror of Web's `WorkAuthorshipFormatter.Display`, kept in Mobile.Cache for unit-testability without the MAUI runtime) and rewired `ScanPage.FoundAuthors` + `BuildWorkRow` to use it. Surprise during implementation: the existing `string.Join(", ", book.AllAuthors)` had been silently broken since the 2026-05-23 wire rename — it was invoking record `ToString` and rendering "AuthorContribution { Name = ..., Role = ... }, ..." on every multi-author scan. That fix shipped in this PR too, fortuitously. 11 new tests (8 formatter + 3 cache round-trip). 459 + 57 green.

## Bucket 3 — Pre-reference data-model wins — **SHIPPED 2026-05-24**

Single PR (`feat/edition-number-reference-status`). **`Edition.EditionNumber: int?`** — nullable column, one migration (no data backfill), `MudNumericField` on `EditionCopyForm`, rendered as `"3rd ed."` on Book Detail's edition row and `"Hardcover · 3rd ed."` on ScanPage. **`BookStatus.Reference`** — fourth enum value (Color.Tertiary chip). Rating stays available (Drew's call: a security reference, an art monograph, or a quality dictionary all deserve ratings — pivoted from the original "hide rating" framing). Wire fields `EditionSnapshot.EditionNumber` + `CachedBookEdition.EditionNumber` added; no `InitAsync` backfill needed (int? defaults to NULL safely, unlike the JSON-string columns that motivated [[sqlite-net-pcl-schema-backfill]]). New `BookTracker.Shared.Formatting.OrdinalFormatter` (`Ordinal(int)` + `OrdinalEdition(int)`) is the single source of truth for the "1st / 2nd / 3rd / ... 11th / 12th / 13th ... 21st" ordinal logic — the 11/12/13 teen special case is too easy to omit in a duplicate, so it lives in Shared and is called by both Web Detail.razor and Mobile ScanPage. 24 new tests (462 + 79 green). Three other TODO #51 nuances (multi-volume sets, corporate authors, full editor model) stay deferred — workable today, only escalate if real capture pain surfaces.

## Memory status — **RETIRE AFTER NEXT SYNC**

All three buckets are now closed (1 deferred, 2 shipped, 3 shipped). Reference-book mass capture can begin. The load-bearing purpose of this memory is fulfilled — it can be removed from `MEMORY.md` on the next sync. Keeping the file around as a historical snapshot of the 2026-05-22 → 2026-05-24 arc; if a future "what's next" conversation surfaces a similar bucket-stacking pattern, the structure is reusable.
