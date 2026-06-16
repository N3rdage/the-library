---
name: feedback_review_at_arc_end
description: For a planned multi-PR arc, run the high-effort code review once at ARC END, not per-PR — mid-arc reviews flag the next PR's work as false negatives.
metadata:
  type: feedback
---

For a planned multi-PR arc, defer the high-effort `/code-review` to **arc end** rather than running it after every PR. Run a per-PR review only when that PR is independently shippable and not part of a known-incomplete sequence.

**Why:** mid-arc, the reviewers repeatedly flag things that are simply *the next PR's job* — transitional pages still present, a helper not yet shared, an affordance deliberately stubbed. Those are false negatives that burn review turns and noise up the findings. Run once at close and the review sees the final state with the scaffolding already removed. Drew, 2026-06-16: *"will run the code review at the end of the arc so we stop getting 'thing we are doing in the next PR' false negatives."*

**How to apply:** when a session is executing a written brief / planned arc, say so and propose the single end-of-arc review; don't reflexively offer a review after each merge. Independent one-off PRs still get their own review. This is about *when* to review, not whether — and it composes with [[feedback_review_findings_gate]] (when you do review, present findings and WAIT for the go-ahead before changing anything). Surfaced in [[retro_bookshelf_redesign_arc]].
