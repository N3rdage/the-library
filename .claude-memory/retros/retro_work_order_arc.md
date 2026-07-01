---
name: retro_work_order_arc
description: Work-order arc retro — per-book BookWork.Order + reorder UI + mobile propagation (3 PRs); drag bug was a config attribute not index math, and adding an invariant conscripts existing violators.
metadata:
  type: project
---

# Per-book Work ordering arc — retro

Dogfooding multi-Work anthologies surfaced two gaps: a book's Works had no stable
order (the read handler sorted alphabetically), so you couldn't tell if all 50
stories were captured; and there was no way to fix a misplaced one. Shipped
2026-07-01 as 3 PRs: **model** (#406), **reorder** (#408), **mobile** (PR3).
Gave the Book↔Work join an explicit `BookWork.Order` (per-book — a Work lives in
many books) and made Works render in capture / user-reordered order everywhere.

## Durable lessons

- **The existing explicit-join was the template.** `WorkAuthor` (Work↔Author with
  an `Order`) had already paid the "promote a skip-nav many-to-many to an explicit
  join entity, keep both navs" design tax. `BookWork` was a near-verbatim copy of
  that shape. When a second instance of a pattern is cheap, it's because the first
  one generalised — look for the precedent before designing.
- **Keep the FK column names → the migration is add-only.** Mapping the explicit
  entity's `BookId`/`WorkId` properties back to the original skip-nav column names
  (`BooksId`/`WorksId`) via `HasColumnName` turned a scary rename/re-key migration
  into a pure `AddColumn` + `ROW_NUMBER` backfill. Data-safe, and the reviewer had
  nothing to flag on the migration.
- **Making a new collection canonical splits the in-memory representation.** Once
  `BookWorks` (not the `Works` skip-nav) carried the order, the ref-count lifecycle
  (`Create`/`AppearsIn`/`RemoveFrom` + orphan check) had to move onto it too — and
  the pure-domain unit tests, which asserted on the skip-nav, went red. In-memory
  there's no EF fixup, so `work.Books` stays empty when the code only writes
  `work.BookWorks`. **Tests must assert the collection the code actually writes**,
  not the one that happens to be populated after an EF round-trip.
- **The drag bug was a missing config attribute, not index math.** Two blind
  attempts at `IndexInZone` arithmetic failed. The fix was `AllowReorder="true"`
  on the `MudDropZone` (without it the zone never computes a positional drop
  index) — found by reading MudBlazor's *own* working reorder example, not by
  reasoning. For a third-party JS/interop feature, **copy the library's known-good
  example verbatim before theorising.** Corollary: I can't test browser drag
  headless, so the user was my test harness across three round-trips — a
  Playwright E2E (TODO #15b) would have closed the loop myself. See
  [[feedback_maui_native_control_sizing]] for the sibling "native/JS control
  ignores your mental model" trap.
- **Adding an invariant retroactively conscripts every existing violator.** The
  PR1 review follow-up added a doc-comment forbidding the `Book.Works = […]`
  initializer. That instantly made `BulkAddViewModel` (which still used it, benign
  at one-Work-per-Book) a *documented-invariant violation* — caught by the arc-end
  review, not the sweep that introduced the rule. A new rule is also a new
  grep-list. [[feedback_grep_list_is_a_checklist]]
- **A read tiebreaker can be the backfill.** Adding `WorkSnapshot.Order` (trailing
  default 0) + sorting `Order` **then `WorkId`** on the mobile side meant legacy
  cached rows (all 0 after the sqlite `ALTER`) fall back to exactly their old
  `WorkId` order — no `InitAsync` one-shot `UPDATE` needed, unlike
  [[feedback_sqlite_net_pcl_schema_backfill]]. The tiebreaker *is* the backfill
  when the legacy sentinel already sorts correctly; reordered books re-sync real
  values via the `UpdatedAt` bump.
- **Risk-bearing PR reviewed pre-merge, the rest at arc close.** PR1 (migration +
  write-boundary cutover) got its own review before merge; PR2/PR3 rode into one
  whole-arc review at close (0 correctness bugs, 2 cleanup). Matches
  [[feedback_review_at_arc_end]] + risk-tracks-write-boundary — review altitude
  tracks the migration/data risk, not the diff size.

## Blog raw material

- "the drag bug that was a config flag" — three attempts at index math, fix was one
  attribute from the library's own example; reasoning lost to reading.
- "the invariant that made old code wrong" — documenting a rule retroactively
  turns benign existing code into a violation; a doc-comment is a grep-list.
- "the tiebreaker is the backfill" — when the legacy default already sorts right.
