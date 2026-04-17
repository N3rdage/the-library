---
name: Memory changes excluded from PRs
description: Don't stage .claude-memory/ files in feature commits. Flag memory changes for a separate commit.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
When committing feature work, do not stage files in `.claude-memory/`. Memory changes should not be mixed into feature PRs.

If memory files have been added or modified during the session, mention it when handing off the branch: "Memory files were also updated — want to do a separate commit for those?"

**Why:** Memory files are workflow metadata, not feature code. Mixing them into feature PRs adds noise to diffs and code review.

**How to apply:** When running `git add`, explicitly list feature files rather than using `git add .` or `git add -A`. After the feature commit, check `git status` for `.claude-memory/` changes and flag them to Drew.
