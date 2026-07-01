---
name: feedback_review_at_arc_end
description: For a planned multi-PR arc, run the high-effort code review once at ARC END, not per-PR — mid-arc reviews flag the next PR's work as false negatives.
metadata:
  type: feedback
---

For a planned multi-PR arc, defer the high-effort `/code-review` to **arc end** rather than running it after every PR. Run a per-PR review only when that PR is independently shippable and not part of a known-incomplete sequence.

**Why (fewer false negatives):** mid-arc, the reviewers repeatedly flag things that are simply *the next PR's job* — transitional pages still present, a helper not yet shared, an affordance deliberately stubbed. Those are false negatives that burn review turns and noise up the findings. Run once at close and the review sees the final state with the scaffolding already removed. Drew, 2026-06-16: *"will run the code review at the end of the arc so we stop getting 'thing we are doing in the next PR' false negatives."*

**Why (catches a category per-PR reviews can't):** point the arc-end review at the *whole-arc diff* (`git diff <arc-base>..HEAD`), not each PR's slice — and it finds bugs the per-PR diff reviews are structurally blind to. A refactor that moves a concept (e.g. series membership Work→Book, TODO #56) breaks code that handled that concept **implicitly and never named it**: `MergeBooks` preserved a book's series for free by unioning the loser's Works — nothing in it said `Series`, so no grep and no per-PR diff ever touched it, yet once series became a Book scalar the union became a silent no-op / data-loss path. Only a holistic "what writes/reads this concept in the *final* state?" pass over the full diff catches those, plus net-state misses (a fix that was only half-applied across PRs). The series-on-Book arc-end review caught two such data-loss bugs that three prior per-PR review rounds had not. Surfaced in [[retro_series_on_book_arc]].

**How to apply:** when a session is executing a written brief / planned arc, say so and propose the single end-of-arc review; don't reflexively offer a review after each merge. Independent one-off PRs still get their own review. This is about *when* to review, not whether — and it composes with [[feedback_review_findings_gate]] (when you do review, present findings and WAIT for the go-ahead before changing anything). Surfaced in [[retro_bookshelf_redesign_arc]].
