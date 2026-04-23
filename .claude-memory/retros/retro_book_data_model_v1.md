---
name: Retro — first book data model (Book → Copy)
description: PR #4 split a flat Book into Book + BookCopy with ISBN/format/condition; the first model decision that shaped every subsequent refactor
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit message of PR #4 (2026-04-14).

**Shipped** — split the original flat Book into two entities: Book holds work-level data (title, author, genre, notes, default cover) and BookCopy holds per-physical-instance fields (ISBN, format, date printed, condition, optional cover override). Status / Rating / DateAdded stayed on Book. Default condition `Good`.

**Surprise (inferred)** — the very first data-model decision on the project was already wrestling with the work-vs-copy distinction. It picked a 2-level model (Book + Copy) which would survive for ~30 PRs before the Edition + Copy refactor (PR #42) and then the Work refactor in this session. Each refactor was strictly *additive* in semantic richness — nothing the v1 model captured was lost, just stratified into more layers as the use case clarified.

**Lesson** — getting the right level of normalisation upfront is hard. The v1 "Book + BookCopy" was the simplest thing that distinguished work from physical instance, and that turned out to be the right *direction* even though it needed two more passes (Edition split, then Work split) to get to a model that handled compendiums, multiple printings, and pen names. The take-home isn't "we should have got it right first time" — it's that each refactor was small because we'd preserved the underlying intuition. Direction-correct beats final-correct.

**Quotable** — Book → BookCopy was the project's first commit-co-authored-with-Claude-style data design. Three months later we'd added Edition and Work above it, but the original split survived intact at the bottom.
