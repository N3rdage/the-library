---
name: Close TODOs in the same PR that delivers the work
description: When a PR delivers a TODO row, move that row from Open → Shipped in TODO.md inside the same commit. Don't defer the bookkeeping.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When a PR closes a TODO row, the TODO.md bookkeeping (Open table row deletion + new Shipped table row) lands in the SAME PR / commit as the code change. Don't leave it for a separate "sync TODOs" pass.

**Why:** Drew set this rule 2026-05-13 after PR 6 of the Add/View/Edit polish arc — "Also happy for you to close off the TODO item that this PR delivers in the same PR, feels neater going forward." Avoids the slow drift where Shipped diverges from reality, and means the same PR review surface shows both the delivery and the housekeeping.

**How to apply:** for any PR that delivers a TODO row:
1. Remove the row from the Open table in `TODO.md`.
2. Add a Shipped table row at the top of `## Shipped` with the same `Category` and `Name`, a `Description` summarising what landed (arc-level for multi-PR work; PR-level for a single PR), the `Estimate` (size) from the Open row, and an `Actual` column with PR numbers / count.
3. Also patch any other open TODO row that referenced the now-closed one (search the doc for the closed number, e.g. "#26", before commit).
4. Stage `TODO.md` alongside the code in the same commit.

Standalone "TODO cleanup" PRs are still fine for batches of bookkeeping that lag behind (e.g. moving multiple older rows in one pass). The rule is about *new* deliveries — don't ship code without also moving the row.

Cross-reference: `feedback_todo_tracking.md` (TODO.md is the master); `feedback_memory_commits.md` (memory still ships separately — different rule).
