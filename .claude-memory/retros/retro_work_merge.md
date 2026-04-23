---
name: Duplicates — Work merge + Attach-existing-Work (PR 3)
description: Two bundled pieces — transactional Work merge with explicit overlap surfacing, plus a typeahead on Edit Book for attaching existing Works. Clean run; first PR in the dedup series with no mid-flight corrections.
type: project
---

## Shipped

PR #88 — 13 files, 19 new tests, 172 → 191 total. `/duplicates/merge/work/{a}/{b}` transactional merge with a side-by-side review page; explicit handling and preview surfacing of the "Book contains both" overlap case. Edit Book "Other works" card gains a typeahead row above the existing create-new row for attaching Works already present elsewhere in the library (the anthology-building flow from the original plan).

## Surprise

- **Nothing surprised, first time in the dedup series.** The InMemory transaction warning from PR 2 was already suppressed; the pending-change-visibility footgun didn't apply to the merge shape (we iterate a loaded collection and clear it, rather than mutating + re-querying). Tests passed on first run. The `MergeAsync_drops_loser_from_books_that_already_contain_winner` test exercised the edge case Drew explicitly wanted surfaced — that went green too.
- **The incompatibility-refusal code path is defensive-only in practice.** Detection blocks by `AuthorId`, so the two Works presented to the merge page always share an author. The refusal path exists for direct-URL hits. Kept it anyway because defense-in-depth is cheap here and a direct-URL merge of works with different authors would silently trash the data.
- **The attach-existing typeahead is 20 lines of Razor and a 2-method VM addition.** Far simpler than I'd braced for — the project's existing pattern (VM owns state, page binds + calls methods, keyup fires search) scales cleanly without needing a separate component. Drew explicitly asked for min-2-char gating; set it in the service, not the page, so direct callers can't accidentally query on a single character.

## Lesson

- **Explicit edge-case surfacing in preview + success pays for itself.** Drew's "yes, surface it explicitly" on the "Book contains both" case made the preview alert + the post-merge banner both inherit a clear sentence each: "N reassigned, M already contained both (winner kept, loser dropped)". Costs three lines of Razor + one extra field on the result record. The alternative (silent correct behaviour, user notices Works count dropped) would have been a quiet bug waiting to happen on any compendium with near-duplicate entries.
- **Bundling works when the seams are clear.** PR 3 paired Work merge with Attach-existing-Work based on a fuzzy "both touch Works" heuristic. In practice the two pieces share a service (`WorkSearchService` — which turned out to only be used by the attach flow) but no UI. That's *less* sharing than I pitched in the original plan. The bundle still justifies itself: one migration surface, one review pass, one merge. If either had needed substantially more work the bundle would have been the wrong call. At ~13 files in one commit, it was fine.
- **Strict-replace merge semantics were the right default.** Earlier plan explicitly said "user picks winner + copies anything they want from loser manually." The merge service does NOT auto-enrich the winner from loser's populated fields. Tempting to add ("if winner has no subtitle but loser does, take it") but that introduces surprise + edge cases (what if both are populated differently?). Strict replace is predictable; the user pays the price of a manual copy step, but that price is about 30 seconds and zero surprises.
- **Test-infra fixes from earlier PRs pay ongoing dividends.** The `TestDbContextFactory` transaction-warning suppression from PR 2 meant I didn't hit that wall again. Similarly, the InMemory `Nullable.Value.Year` workaround pattern (project raw, compute client-side) from PR 1 informed how I wrote `WorkMergeService.LoadDetailAsync`'s `FirstPublishedDate?.Year` projection. Retros help — looking back at the Author merge retro before starting this one saved me both issues.

## Quotable

Three merge PRs (Author, Work, and the upcoming Book and Edition), same shape: load both sides with counts and samples, user picks winner via radio, preview the impact including any edge cases, single-transaction commit with counters in the result, redirect with query-param banner. The shape works. Once the first one lands, the rest are variations on a theme — and the variations are where the interesting design decisions live (alias-compatibility for Authors, "Book contains both" for Works, and whatever surfaces in the next two).
