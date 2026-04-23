---
name: Retro — BookCopy → Edition + Copy refactor
description: PR #42 split the flat BookCopy into a proper Book → Edition → Copy hierarchy. First major data-model refactor; precursor to the Work refactor in this session.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit message of PR #42 (2026-04-17).

**Shipped** — split the flat BookCopy into a proper hierarchy: `Book → Edition (ISBN, Format, Publisher, DatePrinted, CoverUrl) → Copy (Condition, DateAcquired, Notes)`. Edition.ISBN got a unique index. Data migration preserved all existing BookCopy rows: each became one Edition + one Copy; same-ISBN BookCopies merged into a single Edition with multiple Copies.

**Surprise (inferred)** — the *deduplication-on-migration* step is the interesting bit. Existing data had repeated ISBNs (multiple "BookCopy" rows for the same physical edition); the migration consolidated them by ISBN into one Edition + N Copies. This was the first time the project did "data migration with semantic merging" rather than "just rename a column" — and it became the muscle memory we then used in the Work refactor's seed migration (which had its own dedup-on-tuple bug surface in this session).

**Lesson** — every data-model refactor that combines existing rows needs to ask: "what does the dedup key look like, and what happens when source rows have it equal?". Here: `Edition.Isbn` was the natural dedup key. In the later Work refactor (this session), `Title+Subtitle+Author+SeriesId` was *not* unique enough — that was the bug. The lesson goes back to PR #42: when merging on a key, *prove* the key uniquely identifies what you're merging on.

**Quotable** — `BookCopy → Edition + Copy` is the project's textbook example of "the right model emerges by use, not upfront design". v1 was Book + Copy. v2 (this PR) was Book + Edition + Copy. v3 (this session) was Book + Work + Edition + Copy. Each step was small.
