---
name: Retro — hierarchical genre taxonomy
description: PR #11/#12 — curated 48-genre seed list with parent/child structure, rather than free-text tags
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit messages of PRs #11, #12 (2026-04-14).

**Shipped** — replaced free-text genres with a curated hierarchical preset: 48 top-level + sub-genres (Fantasy → High Fantasy / Urban Fantasy / Dark Fantasy …, Mystery → Cozy / Hard-Boiled / Police Procedural …, etc.). Books pick from the preset list via a tree-shaped picker that auto-selects the parent when a child is selected. Source-of-truth in `GenreSeed.cs`; migrations seed/clean from there.

**Surprise (inferred)** — the *taxonomy* of "what counts as a genre" turned out to be load-bearing for most of the AI features later. The Anthropic genre-suggestion service (PR #45) constrains Claude to suggest only from the preset list, which is what makes the response usable as direct DB inserts rather than free-text that needs reconciliation. Locked-down vocabulary upfront → AI features are smaller and more reliable later.

**Lesson** — when picking between "free-text + dedupe later" and "curated vocabulary + extend explicitly", the curated path costs more upfront but compounds positively. Tags (PR #13) went the other way — free-text, no taxonomy — because tags are user-personal labels not categorical claims. Pick the model based on *what's the source of truth*: the user's intent (tags) or a domain consensus (genres).

**Quotable** — adding "Cthulhu Mythos / Vampire / Zombie" as Horror sub-genres in this session (PR #71) was a single 3-row file edit + one idempotent EF migration. Drew expected friction; got none. That's the genre seed paying ongoing dividends.
