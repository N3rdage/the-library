---
name: review-agents-need-diff-file
description: Background review subagents can't reliably run git themselves; hand them a pre-generated diff file to Read.
metadata:
  type: feedback
---

When fanning out code-review finder subagents, don't rely on them running `git diff` themselves. A background subagent's FIRST Bash call hits "Permission to use Bash has been denied" (no interactive approver in an async context), and a finder that treats that as fatal silently gives up — reviewing off its own partial understanding instead of the real delta. Git isn't sandbox-blocked (later Bash calls in the same agent succeed); it's a first-call permission-prompt artifact.

**Why:** root-caused 2026-06-22 during the back-end DDD arc review — one finder gave up on git, and finders that fell back to reading current file contents saw no unified diff, so the removed-behaviour angle (which needs the `-` lines) was blind. See [[retro_backend_ddd_pilot]].

**How to apply:** in the MAIN agent (which has approved Bash), generate the unified diff to a gitignored file — e.g. `git diff <base> HEAD > .debug/<name>.diff` — and tell each finder to **Read** that file as the authoritative delta (Read never touches the Bash permission gate). Also point them at the source files for context, and tell them NOT to run git. Clean up the temp diff when done. Optionally allowlist `git diff` in project settings so subagents can self-serve.
