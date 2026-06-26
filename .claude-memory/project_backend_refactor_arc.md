---
name: project-backend-refactor-arc
description: "Progress + next steps for the DDD/CQRS \"pragmatic spine\" back-end refactor arc (TODO"
metadata: 
  node_type: memory
  type: project
  originSessionId: e24ce96e-656e-429c-bb6b-16afa01f73d9
---

The pragmatic-spine DDD/CQRS refactor (TODO #55, design in `docs/BACKEND-REFACTOR-DESIGN.md`, conventions C1–C12). Each PR: rich aggregate (invariants on the entity) + `BookTracker.Application` command handlers + thin hand-rolled `IDispatcher` (convention-scan registration, no MediatR); ViewModels inject `IDispatcher` and dispatch, signatures unchanged so Razor/dialogs are untouched.

**Done (merged):**
- PR1 Book aggregate pilot · PR2 Work (ref-count C11) · PR3 Series (#362/#363) · PR4 Wishlist (#364) · PR5 Merge ops as commands (#367 — Book/Work/Author/Edition `Merge*` command handlers; merge services trimmed to read loaders; shared `AuthorMergeCompatibility`).
- **PR6a — read-model relocation (pushed 2026-06-26, `feat/read-models`).** Established the **read side**: `IQuery<T>`/`IQueryHandler<,>`/`IDispatcher.Query` (mirrors command side, same convention scan). Relocated to `Application`: both snapshots → `Application/Catalog/` (`GetCatalogSnapshot`/`GetWishlistSnapshot`; `BuildInfo` stays host-side, passed in as `Version`), the 4 merge loaders → `Get*MergePreview` query handlers (merge VMs now **dispatcher-only**), and `WorkAuthorshipFormatter`+`SeriesOrderParser` → `Application/Formatting/` (~18 callers). Read handlers use `AsNoTracking()`+DTO (C5). Tests → `Application/<Feature>/*HandlerTests`. High-effort review: 0 correctness defects (1 maintainability finding → **TD-16** cover-pick dup; stale-using nit fixed).

**Remaining — PR6b (display VMs → off DbContext), then close-out.** Drew chose **Option B** (finish each VM off DbContext: reads→queries AND stray writes→commands) in **~4 grouped sub-PRs**, each branched from fresh `main`:
- **6b-1 Dashboard+simple lists** (Home/Series/AuthorList — read-only; pattern-setter). Extract a shared **author-rollup-count** query (AuthorList + Home + snapshot all duplicate it). Gate-check the display-read DTO convention here.
- **6b-2 Author-admin + Publisher** (AuthorDetail/PublisherList): reads→queries; writes→commands (`RenameAuthor`/`MarkAuthorAsAliasOf`/`PromoteAuthorToCanonical`; `RenamePublisher`/`DeleteUnusedPublisher`/`MergePublishers`—transactional, mirror PR5).
- **6b-3 BookDetail**: `GetBookDetail` (6-Include hierarchy = doc's example) + `GetTagSuggestions`; `AddTagToBook`/`RemoveTagFromBook` via a new shared **`TagResolver`** → **closes TD-15** (also retrofit `MarkWishlistItemBought`'s inline follow-up-tag find-or-create).
- **6b-4 BookList + Wishlist** (heaviest): BookList filter/list/**3-path grouping** queries (pay down **TD-6** while rewriting) + status/rating writes already commands; Wishlist `GetWishlist`/`GetSeriesGaps`(slot-occupancy math)/`FindWishlistDuplicates`, external `IBookLookupService` stays. Split BookList out if too big.

**Survey finding (2026-06-26):** 3 "display" VMs still did **direct DbContext writes** that slipped past PR1–5 — BookDetail tags, AuthorDetail admin, PublisherList admin (Publisher.Merge is transactional). That's why 6b is Option B not reads-only. None of the VM read DTOs leak EF entities (good). VMs already injecting `IDispatcher`: BookList/BookDetail/Wishlist; the other 5 don't yet.

**PR_close (after 6b-4):** the **high-effort whole-arc review** (Drew's "whole arc PR once the reads land"), retro to memory, update `ARCHITECTURE.md`, move TODO #55 Open→Shipped, blog candidates.

**Process notes:** per-PR review before push on PR3/4/5/6a (all 0 reachable bugs; method [[feedback_review_agents_need_diff_file]], gate [[feedback_review_findings_gate]]). Recurring fixes: aggregate factories not raw `new` (C12); centralise duplicated predicates ([[feedback_overloaded_field_shared_predicate]]). Pilot lessons [[retro_backend_ddd_pilot]]. Tech-debt ledger `docs/TECH-DEBT.md` (TD-15 TagResolver lands in 6b-3; TD-16 cover-pick + TD-6 grouping are 6b-adjacent).
