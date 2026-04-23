---
name: Retro — library groupings (Author / Genre / Series accordion)
description: Drew's library was hitting "raw list is overwhelming" — added groupings with lazy-loaded per-group pagination
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — Group-by picker on `/books` (Author default, plus Genre / Series-or-Collection / None). Collapsible Bootstrap accordion of groups, each with a count badge in its header. Per-group lazy load on first expand + per-group pagination (20/page). Filters apply within groups so picking "Mystery" + grouping by Author shows each author's mystery count. Author grouping rolls Bachman titles up under King.

**Surprise** — the canonical-rollup query I'd just written for the Authors page was almost exactly what HomeViewModel needed for top-authors AND what BookListViewModel needed for the Author group keys. Same `GROUP BY w.Author.CanonicalAuthorId ?? w.Author.Id` shape three times. Tempting to extract a helper but each was just 3 lines of LINQ over slightly different projections — not worth the abstraction.

**Lesson** — pagination model question (per-group vs whole list vs none) is the kind of decision that's easy to defer-and-regret. I asked Drew upfront — he picked B (per-group, 20/page) over A (no pagination) because his library was about to grow to 500–1500 books in 4 weeks. Without that "expected scale" data point I'd have shipped option A and rebuilt later. Ask "what's the size in 4 weeks?" not "what's the size now?".

**Quotable** — the moment we realised the same canonical-rollup pattern was needed in 3 places back-to-back. Felt like the schema was just *right* — small entities, small queries, the data wanting to flow that way.
