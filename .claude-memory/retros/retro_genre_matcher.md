---
name: Retro — genre matcher saga
description: Christie books mis-tagged Science Fiction; the bug was a 6-line bidirectional Contains() that had been there for months
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — `FuzzyGenreMatch` rewrite from letters-only `nc.Contains(np) || np.Contains(nc)` to a word-bounded regex `\bpreset\b` against the lowercased original. New `GenreCandidateCleaner` filters known non-genre subjects ("Romance languages", "Fiction", year strings) at the lookup layer. Second backfill service (`BookGenreBackfillService`) cleared every existing book's genres and re-derived them with the new logic.

**Surprise** — the matcher had two distinct over-eagerness modes layered on top of each other and we only spotted them by enumerating actual data. (1) reverse direction: "Science" matched "Science Fiction" because `np.Contains(nc)`. (2) substring not word boundary: "Romance languages" matched "Romance" because that's a real word inside it. Each needed a different fix; missing either left half the false positives in place.

**Lesson** — when reading user-reported library data ("Christie is showing as Sci-Fi"), enumerate the actual subject strings in question before guessing the cause. Open Library returns much messier subject lists than you'd think — "20th century, English fiction, Romance languages, Open_syllabus_project". Rule-based matching of strings into a curated taxonomy needs both a tightener (regex) AND a denylist (Drew's data has surface-level noise the matcher will never disambiguate cleanly).

**Quotable** — the marker-table pattern from the format backfill paid off the very next PR. We didn't have to think about how to make the second backfill safe — just `MaintenanceLog where Name = "BackfillBookGenres-v1"`.
