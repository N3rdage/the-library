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

## Bucket 2 — WorkAuthor cutover Phases D + E (Mobile)

Deferred from PR 2d's scope. Phase D extends `CachedBook` / `BookSnapshot` consumption on the Bookshelf side to handle the new role-tagged `Contributors` array. Phase E surfaces non-Author roles on the MAUI `ScanPage` Book card. Both touch the cache schema (PR 2d ships `AuthorContribution` on the wire but the cache only deserialises the flat `AllAuthors` shape today) so [[sqlite-net-pcl-schema-backfill]] applies — column ALTER needs paired one-shot UPDATE in `CatalogCache.InitAsync`. Sized M as a pair.

## Bucket 3 — Pre-reference data-model wins (subset of TODO #51)

The two smallest sub-rows of TODO #51 that earn their keep *before* mass capture rather than after:

- **`Edition.EditionNumber`** (`int?`) — *Joy of Cooking 1975 vs 2019* differentiation. Today this lives in `Work.Subtitle` or is implicit in `Edition.DatePrinted`. Tiny migration; surface as a numeric field on `EditionCopyForm`. Captured-then-retrofitted means re-keying every reference Edition by hand.
- **`BookStatus.Reference`** — opt reference rows out of the Read/Reading/Unread UI (dictionaries are not "Unread"). Single enum value + UI tweak on the status chip picker to grey out / hide the rating field when status=Reference. Could ship as one PR with EditionNumber.

The other three TODO #51 nuances (multi-volume sets, corporate authors, full editor model) stay deferred — workable today, only become priorities if real capture pain surfaces.

## What this memory is NOT

Not a list of "all open work" — TODO.md is the master, and other items (security hardening, perf observability, a11y review) sit independent of the reference-capture arc. This memory is just the load-bearing pre-reference ordering. Once reference capture is underway, this memory becomes stale and should be retired.
