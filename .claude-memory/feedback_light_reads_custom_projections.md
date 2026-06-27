---
name: feedback-light-reads-custom-projections
description: Drew's call — read models should be light SQL projections to custom shapes; display-count semantics may converge rather than carry complexity for rare numeric perfection.
metadata:
  node_type: memory
  type: feedback
---

For read-side query handlers, the heavy lifting belongs in SQL and the payload should be light: project to **custom read-model records** (not EF-entity-bound graph loads), even when the projection doesn't mirror the table shape.

**Why:** scale target (3000+ books/copies); `.Include(...)` graphs hydrate columns/rows the display never uses (e.g. every Copy just to show a count, or a cartesian Tags×Genres×Authors explosion).

**How to apply:**
- Prefer `.Select(...)` projections over `.Include(...)` + in-memory map. Scalar aggregates translate (`b.Editions.Sum(e => e.Copies.Count)` → SQL, zero Copy rows transferred); nested collection projections translate in EF Core 10; project Series/Publisher fields **flat** (`SeriesId` + `SeriesName` + `SeriesType`), not as a nested conditional anonymous object, to stay translation-safe. C# formatters run over the materialised projection afterward.
- **"Count distinct per group" gotcha — prefer the correlated subquery, NOT the pair-materialise.** EF Core 10 walls on BOTH `Select(pair).Distinct().GroupBy(key).Count()` AND `GroupBy(key).Select(g => g.Select(v).Distinct().Count())` (`could not be translated`). The shape that DOES translate, and keeps the read light, is a **per-author/per-group correlated `COUNT(DISTINCT)` subquery**: `db.Authors.Select(a => new { a.Id, Books = a.WorkAuthors.Where(wa => wa.Role == Author).SelectMany(wa => wa.Work.Books).Select(b => b.Id).Distinct().Count() })` → one row per group, the distinct-count computed in SQL, zero per-row payload. (This is the shape the old snapshot `directCounts` already used — it needs a nav from the group key down to the rows, e.g. `Author.WorkAuthors`.) The fallback — `Select(pair).Distinct().ToListAsync()` then group-count **in memory** — *works* but transfers one row per (key,value) pair; the close-2b whole-arc review flagged it as a hot-path scale regression on Home + the snapshot. Only reach for it when there's no nav to express the count per-group as a correlated subquery.
- **Display-count semantics can CONVERGE.** Top-N author/genre lists are ~the same however you count (books vs works vs contributions), so collapse three encodings onto one shared definition rather than preserving each. Drew: "not worth the drama of keeping the numbers perfect." The canonical author rollup deliberately drops the cross-member DISTINCT — a book credited to both a canonical and an alias counts once *per member* (1 book in ~2000, the King/Bachman omnibus). Don't carry heroic complexity for a 1-in-2000 edge.

This overrode the arc's verbatim-relocation discipline at close-out: the relocations preserved behaviour exactly, but the *consolidation* pass was explicitly allowed to change query shape and converge numbers. Links: [[project-backend-refactor-arc]], [[feedback_overloaded_field_shared_predicate]].
