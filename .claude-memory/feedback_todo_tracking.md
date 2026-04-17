---
name: TODO tracking conventions
description: All TODOs go in TODO.md. Code TODOs mirror there. "Sync TODOs" means reconcile memory + code + TODO.md.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
All TODO items go in `TODO.md` at the repo root — this is the master list.

When Drew asks to add a TODO, add it to `TODO.md`. If it has a specific code location, also add a `// TODO` comment in the code for local context.

When Drew says "sync TODO items" (or similar), scan:
1. Memory for any TODO-type entries
2. Code for `// TODO` comments (grep for `TODO`)
3. `TODO.md` for the current list

Reconcile all three — add anything missing to `TODO.md`, flag any stale entries that look completed.

**Why:** TODOs were scattered across memory, code comments, and README files. A single `TODO.md` is visible to both Drew and Claude, versioned in git, and has no size limits.

**How to apply:** New TODOs always go to `TODO.md` first. Keep code-level `// TODO` comments for local context but treat `TODO.md` as the source of truth when enumerating outstanding work.
