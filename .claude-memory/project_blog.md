---
name: BookTracker blog project
description: Brief + workflow for the blog Drew is writing about working on BookTracker with Claude. Covers what the blog is for, the corpus to mine from, and how a Claude session should help.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
Drew is writing a blog about building BookTracker with Claude — episodic, around themes like the rules we set up, using memory, the architecture and testing regime, small-PR rhythm, etc. The corpus to mine is already in this memory directory:

- `.claude-memory/retros/` — short post-mortems per shipped feature. Index at `retros/index.md`. Each retro has Shipped / Surprise / Lesson / Quotable lines designed for blog mining.
- `.claude-memory/patterns.md` — recurring conventions distilled into ~10 named patterns with explanations. Mineable for blog structure (each pattern could anchor a post).
- `git log` — full commit history with detailed bodies on most PRs.
- `ARCHITECTURE.md`, `CLAUDE.md`, `TODO.md`, `infra/README.md` — project docs that double as artefacts to reference.

## Working style for blog sessions

When Drew opens a session with "let's work on the blog" or similar:

1. **Don't start writing prose without a brief.** Read `retros/index.md` and skim `patterns.md` first.
2. **Propose 3 candidate post outlines** — different angles on different parts of the corpus. Brief: title, 2–3 sentence summary, which retros / patterns it draws from, who it's for.
3. **Wait for Drew to pick** (or hybridise). Don't draft until selection is explicit.
4. **Drafting**: aim for the post Drew picked, ~1000–2000 words depending on scope. Use the retro `Quotable` lines as candidate pull-quotes. Cite specific PRs by number (`PR #42`) when grounding a claim — readers can follow the link.
5. **Land in `blog/` at repo root** as a single `.md` file per post. Filename `YYYY-MM-DD-slug.md`. (Create the `blog/` directory on first post.)
6. **Treat the post as a feature**: own branch, own PR, the same small-PR / hand-off-for-push rhythm. Drew pushes and merges as usual.
7. **After publish**: capture any insight from the writing process itself in a small retro file under `retros/` so the meta-arc gets tracked too.

## Audience + voice (hypothesised — confirm with Drew on first session)

Probably other developers who are curious about working with Claude / Claude Code on real projects. Likely interested in: collaboration patterns, what worked vs didn't, concrete examples not abstract advice. Voice: honest about both wins and dead ends (the Foundry/Sponsored subscription wall is a good example — write the dead ends up).

## Suggested starter prompt for Drew

In a fresh Claude Code session, this is enough to get going:

> Let's work on the BookTracker blog. Read the retros index and patterns doc, then propose 3 candidate post outlines I can pick from.

The memory directory is loaded automatically; this prompt just signals intent + asks for proposals before drafting.
