---
name: Duplicates — Book merge (PR 5, final core merge)
description: Fifth and final core merge PR in the dedup series. Aggregator shape — Book merge moves everything beneath it (editions, copies, works, tags) and has no structural refusal path. Template held; interpretive call was Rating=0 as "unrated".
type: project
---

## Shipped

PR #90 — 9 files, +875 lines. `/duplicates/merge/book/{a}/{b}` as the fifth merge page in the series. 16 new tests (11 service, 5 VM). Series is now complete across Author, Work, Book, Edition, with the `/duplicates` listing page and detection service underneath.

## Surprise

- **No surprises, for the second time in the series.** Like PR 3 (Work merge), PR 5 was a clean application of the merge template. The interpretive call — Rating=0 as unrated — was flagged in planning and confirmed before coding. Tests passed first run. Build was green first pass. This is the payoff from the surface-area-thrashing in PRs 1/2 (detection normalisation, transaction handling, pending-change-visibility) — those early footguns don't fire anymore, and each subsequent merge is mostly filling in template slots.
- **First merge with no incompatibility refusal path.** Author had alias-mismatch, Work had different-authors, Edition had cross-Book. Book is the *aggregator* — everything beneath it either moves (Editions flip BookId, Copies travel with Editions) or unions (Works, Tags). Nothing to refuse. The absence of a compatibility check felt weird to write after four PRs with one; I double-checked that the ISBN unique-index-across-all-Editions really did make cross-Book ISBN collision impossible. It does. The Book is the top of the merge stack and has nothing above it to constrain against.
- **Post-Book-merge Edition duplicates are expected.** If both Books had no-ISBN Editions with matching format/publisher/date, the Book merge reassigns them both to the winner and they become Edition-level dupes. Those surface on the next `/duplicates` refresh and get cleaned up via Edition merge. Documented this explicitly in both the service comment and the TODO.md. Two-step cleanup is fine when the second step is exactly the tool we shipped in PR 4.

## Lesson

- **Aggregator entities deserve simpler merge semantics.** Four entity types, four merge operations — but Book is different because it sits at the top of the ownership graph in this schema. It has no parent constraint (no Author-above-Book-above-Edition chain where Book would need to reconcile with something). That meant no compatibility check, simpler control flow, and the most straightforward merge service in the series. Spot-worth knowing: when designing a merge for an entity type, the question "what's above this in the ownership graph?" tells you whether the merge can do a bare move-and-clear or needs a compatibility pre-check.
- **The Rating=0 heuristic is a UI-semantics bet, not a data-model fact.** The `int Rating` field can technically hold 0-5, and 0 could mean either "unrated" or "rated zero". The data model doesn't distinguish. The rule "treat 0 as unrated for enrichment" is a bet that the stars-1-to-5 UI can't produce an active 0. If that UI ever changes (e.g. "0 stars to unrate"), the heuristic breaks. Worth a note in the commit, but not worth a separate flag field. Simpler is better until it's not.
- **The "richer sidebar" paid off.** Book's merge card shows more than the other entities — edition/copy counts, works list (first 5), tag badges, notes preview. Because a Book duplicates at the highest level in the aggregate, the user needs the most context to decide which is the winner. The sidebar absorbs that need without a separate drill-down. Template scalability principle: richer entities get richer previews, same card structure.
- **Series retrospective pattern.** Five PRs over ~2 days of active work. Two rounds of mid-series design pivots (Preston matcher-loosening in PR 1, strict-replace→auto-fill-empties in PR 4), each corrected on the next PR. The detection + merge template absorbed each pivot without structural churn. The remaining plan item (PR 6, gap-fill on add-Edition-by-ISBN) now feels optional — the full merge toolkit is enough to handle the state Drew's real data presents. Shipping in small increments with real-data feedback between them is the right rhythm for this kind of feature series.

## Quotable

Five merge PRs, same template, five different per-entity decisions:
- Author: alias-compatibility refusal
- Work: "Book contains both" overlap handling
- Edition: cross-Book refusal
- Book: no refusal (aggregator)

The template is: `LoadAsync` + `MergeAsync` + winner-radio VM + side-by-side page + query-param banner. The decisions are: what's the structural constraint? what's the "obvious" enrichment rule? what sample fields help the user choose a winner? Every new merge-like feature from here can copy the template and answer the three questions — the answers are the interesting work, everything else is paperwork.
