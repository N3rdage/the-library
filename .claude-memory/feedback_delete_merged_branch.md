---
name: Delete merged branches
description: Always delete the local feature branch after switching to main and pulling the merge.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
After a PR is merged, when switching back to main and pulling, also delete the local feature branch with `git branch -d <branch>`.

**Why:** Drew wants a clean local branch list — no stale merged branches.

**How to apply:** Run `git checkout main && git pull && git branch -d <branch>` as a single step when Drew confirms a PR is merged.
