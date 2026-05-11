---
name: Parallelise docs during CI waits
description: When code PR is pushed and waiting on CI/merge, do non-conflicting work (retros, blog posts, TODO updates) on a new branch off main in parallel rather than serialising.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When a code PR is pushed and waiting on CI / merge, don't sit idle until the merge confirmation lands. Work that doesn't touch the in-flight code — retros, blog posts, TODO bookkeeping, memory updates, planning docs — can branch off the current `main` (i.e. the state *before* the in-flight PR) and run in parallel.

**Why:** Drew called this out 2026-05-11 after the mobile-arc PR 6 push. CI on this repo is on the order of minutes; serialising blog/retro work behind every merge wastes that window. Docs and code don't conflict (different files), so a parallel branch + later main pull is safe — no merge surprise.

**How to apply:**
- After pushing a code PR, before saying "standing by," check whether the next ask in the queue (retro, blog post, memory update, TODO move) touches the code in flight. If not, start it immediately on a fresh branch off the pre-push `main`.
- The branch base is *still* the pre-push `main` (you don't have the merge commit yet), so when the PR merges and you `git pull`, the parallel branch will fast-forward / rebase cleanly because it changed different files.
- Push the parallel branch only after the dependency merges — keeps the PR queue ordered and avoids interleaved CI runs.
- If the parallel work *does* touch a file the code PR also touches (rare for docs vs code, but e.g. TODO.md could collide), serialise that one rather than guessing at merge order.

Specifically excludes: anything that depends on the merged code being live (smoke tests against deployed staging, version-stamped CHANGELOG entries, retro lessons that need post-merge confirmation Drew hasn't given yet).
