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
| TD-6 | Library | **Two pre-existing duplications in the grouping code**, surfaced (not caused) by the 2026-06-15 #53c review. (a) `List.razor`'s `GroupBySelected` field is a hand-synced shadow of `VM.SelectedGroupBy` (set in three places); binding the MudSelect directly to the VM would delete the field + both sync sites. (b) `GroupByAuthorAsync`, `PrimaryAuthorGroupAsync`, and `GroupByGenreAsync` each repeat the same "resolve ids→names, build `GroupRow` list ordered by Label" tail — a `ToGroupRows(rawCounts, nameLookup)` helper would collapse the three copies (the retired `GroupBySeriesAsync` was a 4th). | Two sources of truth / three copies must move together; a change to GroupRow keying or ordering touches all of them. Low risk today, fold into the next Library-grouping PR. | S |

## Accepted — deliberately not fixing (for now)

| # | Area | Item | Decision & rationale |
|---|---|---|---|
| TD-A1 | Library | **Author group drill keys on `group.Label` (canonical name), not the canonical id** — unlike Genre/Series which key on `group.Key`. The theoretical `"(unknown)"` label fallback would drill to an empty list. | **Accept.** `Author.Name` is unique (enforced) and `CanonicalAuthorId` is an FK, so the `"(unknown)"` fallback is practically unreachable; name-keying is also *consistent* with the existing name-based author filter shown in the Author autocomplete. Drilling by id would require a new author-id flat filter for no real gain. Revisit only if author-name uniqueness is ever relaxed. (Source: 2026-06-12 review finding #4.) |
| TD-A2 | Library | **An unsubmitted search term is committed to the URL by the next non-search filter edit.** Typing in the search box (which waits for Enter) then changing, e.g., Status serialises the typed-but-not-submitted term too. | **Accept.** Behaviour is unchanged from the pre-URL handlers, and the search box visibly shows the term — committing what the user can see when they touch another filter is reasonable. Revisit if it surfaces as confusing in dogfooding. (Source: 2026-06-12 review.) |
| TD-A4 | Library | **An old `?group=Collection` bookmark/shared link silently degrades to the Author grouping** (the Series grouping was retired in #53c). `Enum.TryParse` fails on the retired token and falls back to the Author default; the stale `group=Collection` token is also left in the URL (the page only rewrites the query on a `None`-grouping page clamp). | **Accept.** Sole-user app, the grouped Series view was short-lived, and the fallback can't recover *which* series was being viewed (the query carries no series id), so any remap would be a guess. Landing on the Author default is a safe, non-crashing degradation; the stale token renders fine and only matters if re-shared. Regression-guarded by `ApplyQueryParameters_FallsBackToDefaultsForAbsentOrJunk`. Revisit only if a real shared-link complaint surfaces. (Source: 2026-06-15 #53c review, F2.) |
| TD-A3 | Library | **Author-group drill no longer clusters books series-then-standalone.** The old accordion's expand sorted an author's books with series-having members first (by series name, then SeriesOrder), standalones tailing alphabetically. Drilling an Author group now lands on the flat list sorted newest-first (DateAdded). | **Accept for now.** The clustering sort lived only in the accordion's `LoadGroupBooksAsync`, which became unreachable when PR2 switched to drill-down, then was deleted in TD-1 (2026-06-12). Collection drill still sorts by reading order (SeriesOrder). If author-drill clustering is missed, restore it by porting the sort into `LoadBooksAsync` when `SelectedAuthor` is set — but that's a feature decision, not debt. |

## Resolved

| # | Item | Resolution |
|---|---|---|
| TD-1 | Remove dead accordion machinery in `BookListViewModel` | Done 2026-06-12. Deleted `ToggleGroupAsync`, `LoadGroupBooksAsync`, `ApplyGroupFilter`, `LoadedGroups`, `ExpandedGroupKeys`, the `GroupBooks` record, and `PatchLoadedItem`'s group loop — plus three page-action helpers the URL rework had orphaned (`ChangeGroupingAsync`, `GoToPageAsync`, `ClearFiltersAsync`). The four `ToggleGroupAsync_*` tests were retargeted onto the flat-list path (two converted, two removed as redundant/obsolete). One deliberate behaviour drop recorded as TD-A3. |
| TD-3 | `loadedSignature` `\|`-delimited string could collide | Done 2026-06-12. Replaced the delimited-string change-detection in `List.razor` `OnParametersSetAsync` with a value-tuple of the query params (element-wise equality — no delimiter to collide on, and a new param is a compile-time tuple change). |
| TD-4 | `CurrentPage` clamp desynced from the URL | Done 2026-06-12. After a flat-list reload, if `CurrentPage` was clamped (result set shrank), the page now writes the corrected page back via `replace:true` nav, keeping the URL the source of truth. VM clamp covered by `FlatList_PageBeyondRange_ClampsToLastPage`. |
| TD-5 | Genre/series sentinel rule triplicated | Done 2026-06-12. Extracted `SentinelToQuery`/`SentinelFromQuery` helpers on the VM; `ToQueryParameters`/`ApplyQueryParameters` now call them instead of inlining the `-1`/`0`/`>0` rule in two dialects. (`ApplyFilters` keeps its per-dimension predicates — a different concern — but the rule is named once.) |

---

_Started 2026-06-12 from the code review of the Library nav rework (PR1 URL filters + PR2 group drill-down). The headline bug from that review — group drill emitting `group=null` (Author default) instead of the flat-list `"None"` token — was fixed in the same branch (`BookListViewModel.BuildGroupDrillParameters`, regression-tested), so it is **not** listed here._
