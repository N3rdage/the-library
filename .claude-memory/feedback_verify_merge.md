---
name: Verify merge on remote before post-merge work
description: When the user says a PR is merged, check git fetch + origin/main before pulling/deleting branches or building follow-up work. The user's message is a claim; the remote is ground truth.
type: feedback
---

When the user reports a merge ("merged", "it's in", "landed"), verify the actual state on the remote before proceeding with post-merge housekeeping or follow-up work.

**Why:** On 2026-04-22, during the Book View PR 1 handoff, the user said "have merged it" but had not actually clicked through the GitHub merge button (forgot to hit Enter in the console). I started post-merge flow (pull, delete local branch) and only caught it when `git pull` showed `Already up to date.` and `git log` showed the PR hadn't landed. Cost was cheap because I checked early; would have been expensive if I'd started building PR 2 on top of a phantom merge.

**How to apply:** After the user reports a merge, the first step is a cheap verification, not a pull/delete. Either:
- `git fetch origin && git log -1 origin/main` — confirm the expected commit SHA or PR squash message is actually there, OR
- `git ls-remote origin refs/heads/<feature-branch>` — if the remote feature branch still exists, the squash-merge (which auto-deletes) hasn't run.

If the remote shows the merge isn't there, say so plainly to the user and wait for them to confirm or re-merge — don't assume it's a sync lag and plough ahead. The same rule generalises to any state-changing handoff (deploy complete, CI passed, migration applied): verify the artefact before acting on the report.
