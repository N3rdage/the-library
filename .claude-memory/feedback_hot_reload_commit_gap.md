---
name: hot-reload-commit-gap
description: "Every mid-PR-cycle change must be committed before the user tests it. Hot-reload makes uncommitted edits feel \"done\" to the user — when they then say \"merged\", the merge only contains what was actually pushed."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 357f34b8-2b9f-4268-b445-cf71fd75fda6
---

When iterating on a PR after the initial push — Drew flags an issue, I edit, Drew checks via `dotnet watch` hot-reload — every accepted change must be committed (and Drew pinged to push the follow-up) before Drew tests it, OR I must explicitly flag "these are uncommitted local-only" each time.

**Why:** PR2 of the MudBlazor reskin arc (#248) shipped without two follow-up fixes (BulkAdd MudTable + AIProviderToggle Mud conversion). Drew flagged them in browser, I edited, hot-reload picked them up live, Drew tested in browser, said "tested and merged" — but those edits were uncommitted local-only. The merge contained only the original commit. Required a separate clean-up PR (#249) just to land what Drew thought was already merged.

**How to apply:** After making any code change during a "review the live PR" cycle, immediately commit on the existing branch and tell Drew to push the new tip *before* asking them to re-test. The hot-reload preview is not a substitute for a git commit. Pairs with [[feedback_commit_locally]] — same rule, but applied to mid-cycle iterations rather than end-of-task hand-off.
