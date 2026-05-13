---
name: TODO tracking conventions
description: All TODOs go in TODO.md. Code TODOs mirror there. "Sync TODOs" reconciles memory + code + TODO.md AND probes each Open row against the codebase for "already shipped, never moved" drift.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
All TODO items go in `TODO.md` at the repo root — this is the master list.

When Drew asks to add a TODO, add it to `TODO.md`. If it has a specific code location, also add a `// TODO` comment in the code for local context.

When Drew says "sync TODO items" (or similar), scan:
1. Memory for any TODO-type entries
2. Code for `// TODO` comments (grep for `TODO`)
3. `TODO.md` for the current list
4. **Each Open row's named artefacts against the actual codebase** — for every Open row that mentions specific files / functions / properties (e.g. "add `SearchBooksByTitleAsync`", "layer four BoxView rectangles in `ScanPage.xaml`"), grep the named bits. If they already exist, the row is stale: move it to Shipped with the PR number from `git log --all -- <file> | grep -i 'TODO #N'`.

Reconcile all four — add anything missing to `TODO.md`, move shipped-but-stale rows to Shipped, flag any rows that look completed but can't be confirmed.

**Why:** TODOs were scattered across memory, code comments, and README files. A single `TODO.md` is visible to both Drew and Claude, versioned in git, and has no size limits. The Open-vs-reality probe (step 4) was added after the 2026-05-13 Bookshelf arc sync session found four Open rows (#30, #31, #32, #35a) that had genuinely shipped weeks earlier on dedicated PRs (#218, #229, #230, #233) — `feedback_close_todos_in_same_pr` post-dated those PRs so the move was missed, and the earlier sync only checked code `// TODO` comments vs TODO.md, not "is the feature actually shipped" against Open rows. Cost a wasted "go ship #30" branch before I noticed the overlay was already in `ScanPage.xaml`.

**How to apply:** New TODOs always go to `TODO.md` first. Keep code-level `// TODO` comments for local context but treat `TODO.md` as the source of truth when enumerating outstanding work. Before starting any "ship TODO #N" work, do step 4 for that row specifically — even when you trust the Open list. The first commit of a feature branch should reveal nothing already done.
