---
name: Always branch from main
description: Before starting any new work, return to main and pull. Never start a new branch from a still-pending sibling branch — sibling-stacking causes squash-merge weirdness and unintentional content bundling.
type: feedback
---

When starting any new branch, the sequence is always:

```powershell
git checkout main
git pull
git checkout -b <prefix>/<name>
```

Even if the previous branch hasn't been pushed yet, even if the new work feels related to the previous, even if the previous branch's content "would be useful as a starting point" — return to main first.

**Why:** With squash-merge as the project's default (and that decision held after a deliberate review on 2026-04-28), branching from a sibling means the new branch's diff against main includes BOTH the sibling's content AND the new work. When the new branch is merged, GitHub's squash flattens both sets of changes into one commit. Past incident (PR #130, 2026-04-27): a chore/todo branch was followed by a blog/retro branch built on top of it; the squash bundled the chore PR's content into the blog PR. End state was correct but the PR title misled, and local cleanup needed `git branch -D` because git couldn't see the squashed-merged content as merged.

**How to apply:**

- After every "merged" confirmation from Drew, before any other action, run `git checkout main && git pull` (the delete-merged-branch rule already mandates this — this rule just adds: *always start the next branch from there*, never from the just-deleted sibling's parent or any other branch).
- If new work has logical sub-parts that ship together, prefer **multiple commits in one PR** (squash preserves all commit subjects in the squash-commit body) rather than stacking branches. The 2026-04-27 incident would have been avoided by this shape — TODO + retro + blog as three commits on one branch.
- If work genuinely needs a stack (PR-B depends on unmerged PR-A's content), say so explicitly to Drew so we plan the merge sequence together — don't create the stack silently.
