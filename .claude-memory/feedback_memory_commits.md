---
name: Memory changes excluded from PRs
description: Don't stage .claude-memory/ in feature commits. Memory lands as its own periodic chore PR, triggered by arc-close, external reference, or monthly housekeeping.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
When committing feature work, do not stage files in `.claude-memory/`. Memory changes should not be mixed into feature PRs.

If memory files have been added or modified during the session, mention it when handing off the branch: "Memory files were also updated — want to do a separate commit for those?"

**Why:** Memory files are workflow metadata, not feature code. Mixing them into feature PRs adds noise to diffs and code review.

**How to apply:** When running `git add`, explicitly list feature files rather than using `git add .` or `git add -A`. After the feature commit, check `git status` for `.claude-memory/` changes and flag them to Drew.

## When memory *does* land

"Don't stage with features" is the rule; the implicit corollary was "never, then," which in practice meant a 46-file gap accumulated over a month before a blog post referenced memory content and forced the issue. The updated rule:

Memory lands as its own small `chore/*` PR when **any** of the following fires:

1. **A feature arc closes and its retro is written.** A "feature arc" is a multi-PR thread (book-detail arc, duplicate-management arc, going-public arc). Land the arc's retro + any feedback / pattern files accumulated during it, together, shortly after the arc's final PR merges. Subject line: `chore(memory): retro + patterns from <arc> arc`.
2. **An external artefact references memory content.** Blog posts, README edits, or any doc that links to `.claude-memory/` files needs those files to actually be public. When such an artefact is prepared, a memory-landing PR goes in *before* the artefact's PR merges — otherwise the artefact ships broken links.
3. **Monthly housekeeping.** If neither (1) nor (2) has fired in ~30 days, do a sweep regardless. Catches drift: new feedback files written in passing, small pattern-doc updates, one-off references that never reached the external-artefact threshold. Subject line: `chore(memory): monthly sweep`.

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
