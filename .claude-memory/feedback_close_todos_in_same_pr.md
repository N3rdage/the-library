---
name: Close TODOs in the same PR that delivers the work
description: When a PR delivers a TODO row, delete that row from the Open table in TODO.md inside the same commit; the merged PR and its backlog issue are the record. Don't defer the bookkeeping.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When a PR closes a TODO row, the bookkeeping (deleting the row from the Open table in `TODO.md`) lands in the SAME PR / commit as the code change. Don't leave it for a separate "sync TODOs" pass. There is no shipped list: the merged PR and its closed backlog issue are the record of what shipped and when, and documents describe the present rather than their own history (Drew's rule, November 2026; `CLAUDE.md` > Branching states it).

**Why:** Drew set this rule 2026-05-13 after PR 6 of the Add/View/Edit polish arc — "Also happy for you to close off the TODO item that this PR delivers in the same PR, feels neater going forward." Avoids the slow drift where the Open table diverges from reality, and means the same PR review surface shows both the delivery and the housekeeping.

**How to apply:** for any PR that delivers a TODO row:
1. Delete the row from the Open table in `TODO.md`.
2. Also patch any other open TODO row that referenced the now-closed one (search the doc for the closed number, e.g. "#26", before commit), so the fact survives without the pointer.
3. Stage `TODO.md` alongside the code in the same commit. Do not add a shipped table, a "done" marker or a dated note anywhere.

Standalone "TODO cleanup" PRs are still fine for batches of bookkeeping that lag behind (e.g. deleting multiple stale rows in one pass). The rule is about *new* deliveries — don't ship code without also deleting the row.

Cross-reference: `feedback_todo_tracking.md` (TODO.md is the master); `feedback_memory_commits.md` (memory still ships separately — different rule).
