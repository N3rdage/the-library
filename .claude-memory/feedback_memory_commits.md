---
name: Memory changes excluded from PRs (with closing-PR exception)
description: Don't stage .claude-memory/ in feature commits — with one exception. The closing PR of a feature arc carries its retro + TODO Shipped move; everything else lands as its own periodic chore PR (external-reference trigger or monthly sweep).
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
When committing feature work, do not stage files in `.claude-memory/`. Memory changes should not be mixed into feature PRs — **with one exception**: the closing PR of a feature arc carries its own retro + the TODO.md move-to-Shipped (see "Exception" section below). Everything else still lands as a separate `chore(memory): …` PR.

If memory files have been added or modified during the session and they're *not* covered by the closing-PR exception, mention it when handing off the branch: "Memory files were also updated — want to do a separate commit for those?"

**Why:** General workflow memory (feedback files, pattern docs, user-profile tweaks) is metadata about *how we work*, not metadata about *what shipped* — it doesn't belong in feature diffs. The closing-PR exception is specifically for memory that documents *the thing being shipped* (its retro) plus the TODO.md row that moves it to Shipped. Bundling those keeps the work and its record atomic.

**How to apply:** When running `git add`, explicitly list feature files. For closing PRs, that includes the retro + `retros/index.md` + `TODO.md`; for non-closing PRs, that excludes everything in `.claude-memory/`. After the commit, check `git status` for unstaged `.claude-memory/` changes and flag them to Drew.

## When memory *does* land

"Don't stage with features" is the rule; the implicit corollary was "never, then," which in practice meant a 46-file gap accumulated over a month before a blog post referenced memory content and forced the issue. The updated rules:

### Exception — closing PR of a feature arc carries its own retro + TODO update

The PR that ships the **last piece of an arc** also lands:

- The arc's retro under `.claude-memory/retros/`.
- The `retros/index.md` link to the new retro.
- The `TODO.md` move-to-Shipped row, including any follow-up TODOs surfaced during the arc and any cross-reference renumbering.

These ride along with the closing PR (one branch, multiple commits if helpful — the squash preserves every commit subject in the squash-commit body). Bundling keeps the work and its full record atomic: when the feature ships, its retro and TODO state ship with it, and there's no "I shipped the feature, then forgot to write the retro / move the TODO row" gap. Locked in 2026-04-28 after the staging-DB-sep arc shipped this way (PR #130 retro + blog + TODO + new follow-ups, all together).

What still doesn't ride along: feedback files, pattern-doc updates, user-profile tweaks, project notes — any general workflow memory accumulated during the arc but not specifically *about* what shipped. Those go in the periodic memory chore PR per the triggers below.

### Other memory still lands as its own `chore(memory): …` PR

Memory not covered by the closing-PR exception lands as its own small `chore/*` PR when **any** of these fires:

1. **An external artefact references memory content.** Blog posts, README edits, or any doc that links to `.claude-memory/` files needs those files to actually be public. When such an artefact is prepared, a memory-landing PR goes in *before* the artefact's PR merges — otherwise the artefact ships broken links.
2. **Monthly housekeeping.** If neither (1) nor a closing-PR exception has fired in ~30 days, do a sweep regardless. Catches drift: new feedback files written in passing, small pattern-doc updates, one-off references that never reached the external-artefact threshold. Subject line: `chore(memory): monthly sweep`.
3. **A blog post is being written.** Blog posts are their own PRs (separate from the closing-arc PR they may reference) — same rules as any other external-artefact case.

## What to include in a memory chore PR

- Untracked `.md` files in `.claude-memory/` (retros, new feedback files, new project facts).
- Modified tracked files in `.claude-memory/` (the `MEMORY.md` index typically).
- **Not:** the `.claude-memory/retros/index.md` if it's untracked (land it with its first retro batch so the index appears alongside the content it describes).

## Safety gate before the memory PR

Treat the memory PR the same way as any other "making something public" change:

1. Run `gitleaks dir --no-banner .claude-memory` — catches any secret-shaped strings.
2. Manual sweep for personal data: old email patterns, phone numbers, specific real-world names beyond those already public (famous authors used as examples are fine; colleagues / clients / friends are not).
3. Spot-check any long-form retro (>20 KB) for verbatim chat transcripts or raw user quotes that might be awkward public.

The gate isn't "is this a security risk" — the security audit covers that. The gate is "is this what we want indexed publicly under the repo's banner." See `SECURITY-AUDIT.md` for the security-specific scan.
