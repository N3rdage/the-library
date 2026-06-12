# Tech Debt

A running ledger of code-quality / robustness items surfaced during reviews and
dogfooding — the stuff that isn't a feature (those live in [`TODO.md`](../TODO.md))
but that we want a record of, so future-us either fixes it deliberately or can
see we **chose** to live with it and why.

Two sections:

- **Open — to address** — known debt we intend to pay down when it's convenient
  (usually folded into the next PR that touches the area).
- **Accepted — deliberately not fixing (for now)** — debt we've looked at and
  consciously decided to leave, with the rationale. Revisit only if the stated
  assumption breaks.

Each item notes where it came from so the context is recoverable.

## Open — to address

| # | Area | Item | Why it matters | Size |
|---|---|---|---|---|
| TD-2 | Library / scale | **`BookQueryWithIncludes` eager-loads four collection navigations in one query** (`Tags`, `Works→Genres`, `Works→WorkAuthors→Author`) — a cartesian-explosion shape that wants `AsSplitQuery()`. Also flagged: the grouped-sort correlated `.Min()` subqueries and FK-index coverage. | Makes every Library load heavier than necessary; bites harder toward the 3000+ copy target. Best handled as part of a `/scale-audit` pass rather than piecemeal — that skill targets exactly this. | M |
| TD-3 | Library / correctness | **`loadedSignature` is a `\|`-delimited concat of raw query values** (`List.razor` `OnParametersSetAsync`). A search term or author name containing `\|` could collide into a false "unchanged" signature and skip a needed reload. | Low-probability stale-list bug; also every new filter param must be hand-added to the concat with no compiler help. Replace with a structured/ordinal comparison of the `ToQueryParameters` dict (or hash it). | S |
| TD-4 | Library / correctness | **`LoadBooksAsync` clamps `CurrentPage` in-VM without re-navigating.** URL says `page=5`, data shrinks to 2 pages → VM shows page 2 but the URL still says 5, breaking the "URL is the source of truth" invariant PR1 established. | Harmless today (re-clamps on refresh) but a latent desync. Fix: clamp before capturing `loadedSignature`, or issue a `replace:true` nav with the corrected page. | S |
| TD-5 | Library / cleanup | **The genre/series `-1` / `>0` / `0` sentinel rule is encoded three times** in three dialects across `ApplyFilters`, `ToQueryParameters`, and `ApplyQueryParameters` (and `tag` uses `>0` while genre/series use `!=0`). | Adding a filter dimension or a second sentinel means editing the same rule in three places; an inconsistency round-trips to the URL but silently no-ops in the query, with no compile error. Consider a small shared helper. | S |

## Accepted — deliberately not fixing (for now)

| # | Area | Item | Decision & rationale |
|---|---|---|---|
| TD-A1 | Library | **Author group drill keys on `group.Label` (canonical name), not the canonical id** — unlike Genre/Series which key on `group.Key`. The theoretical `"(unknown)"` label fallback would drill to an empty list. | **Accept.** `Author.Name` is unique (enforced) and `CanonicalAuthorId` is an FK, so the `"(unknown)"` fallback is practically unreachable; name-keying is also *consistent* with the existing name-based author filter shown in the Author autocomplete. Drilling by id would require a new author-id flat filter for no real gain. Revisit only if author-name uniqueness is ever relaxed. (Source: 2026-06-12 review finding #4.) |
| TD-A2 | Library | **An unsubmitted search term is committed to the URL by the next non-search filter edit.** Typing in the search box (which waits for Enter) then changing, e.g., Status serialises the typed-but-not-submitted term too. | **Accept.** Behaviour is unchanged from the pre-URL handlers, and the search box visibly shows the term — committing what the user can see when they touch another filter is reasonable. Revisit if it surfaces as confusing in dogfooding. (Source: 2026-06-12 review.) |
| TD-A3 | Library | **Author-group drill no longer clusters books series-then-standalone.** The old accordion's expand sorted an author's books with series-having members first (by series name, then SeriesOrder), standalones tailing alphabetically. Drilling an Author group now lands on the flat list sorted newest-first (DateAdded). | **Accept for now.** The clustering sort lived only in the accordion's `LoadGroupBooksAsync`, which became unreachable when PR2 switched to drill-down, then was deleted in TD-1 (2026-06-12). Collection drill still sorts by reading order (SeriesOrder). If author-drill clustering is missed, restore it by porting the sort into `LoadBooksAsync` when `SelectedAuthor` is set — but that's a feature decision, not debt. |

## Resolved

| # | Item | Resolution |
|---|---|---|
| TD-1 | Remove dead accordion machinery in `BookListViewModel` | Done 2026-06-12. Deleted `ToggleGroupAsync`, `LoadGroupBooksAsync`, `ApplyGroupFilter`, `LoadedGroups`, `ExpandedGroupKeys`, the `GroupBooks` record, and `PatchLoadedItem`'s group loop — plus three page-action helpers the URL rework had orphaned (`ChangeGroupingAsync`, `GoToPageAsync`, `ClearFiltersAsync`). The four `ToggleGroupAsync_*` tests were retargeted onto the flat-list path (two converted, two removed as redundant/obsolete). One deliberate behaviour drop recorded as TD-A3. |

---

_Started 2026-06-12 from the code review of the Library nav rework (PR1 URL filters + PR2 group drill-down). The headline bug from that review — group drill emitting `group=null` (Author default) instead of the flat-list `"None"` token — was fixed in the same branch (`BookListViewModel.BuildGroupDrillParameters`, regression-tested), so it is **not** listed here._
