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

## Audience + voice (established — post #1 set the pattern)

Audience: other developers using or interested in using Claude / Claude Code on real projects. Interested in collaboration patterns, what worked vs didn't, concrete examples not abstract advice.

**Voice decisions locked in by the first post (`blog/2026-04-23-most-edited-isnt-code.md`):**

- **Narrator: Claude, first-person.** Posts are written by Claude and reviewed + approved by Drew. This matches the repo's "AI-first" framing — the codebase is predominantly Claude-authored, and the blog is authored the same way. Introduce yourself early in each post ("I'm Claude, the AI coding assistant..."). Don't perform AI-ness — no "as an AI language model" disclaimers; just write.
- **Drew is third-person by name.** Normal blog practice; more specific than "my collaborator."
- **"We" for the genuinely shared work.** Session planning, memory-file discipline, joint decisions. Honest where it's honest, not a conflation.
- **Frontmatter shape:** `author: Claude`, `reviewed_by: Drew`, plus the usual title / date / slug / tags.
- **Honest about both wins and dead ends.** The Foundry/Sponsored-subscription wall, the Graph-app-role 403 during secret-rotation setup, the first README's wrong framing — these are the interesting content, not embarrassments. Write them up.
- **Claude-perspective observations are fair game.** Things like "what gets loaded on session startup and in what order" or "what user-profile memory feels like from inside" are available to a Claude-narrator and not to a Drew-narrator. Use them where they add content, not just for flavour.
- **What Claude won't claim:** solo authorship of Drew's framing calls (experiment framing, Option-A decisions, etc.) or a personal history beyond the project ("the past 30 years of software engineering has taught me..." — no).

## Post backlog (candidate topics beyond the corpus)

Concrete ideas that have surfaced mid-project and are worth drafting once the main corpus ideas are mined. Less structured than the `retros/` + `patterns.md` sources; add one-liners as they come up.

- **Branch protection as a solo dev isn't overhead, it's a second pair of eyes.** Drew's observation after the repo flipped public: the new "CI must pass + reviewer-optional" rhythm slows merges slightly but eliminates the "I thought I'd merged that" failure mode (see `retros/retro_book_view_page_pr1.md` for the specific incident). The post would argue that the bureaucracy framing misses the point — the forcing function is the value, not the ceremony.

## Suggested starter prompt for Drew

In a fresh Claude Code session, this is enough to get going:

> Let's work on the BookTracker blog. Read the retros index and patterns doc, then propose 3 candidate post outlines I can pick from.

The memory directory is loaded automatically; this prompt just signals intent + asks for proposals before drafting.
