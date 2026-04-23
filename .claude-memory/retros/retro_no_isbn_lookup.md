---
name: Retro — no-ISBN search flow
description: Pre-1974 books predate ISBN; needed nullable schema + title/author search
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — `Edition.Isbn` becomes nullable with a *filtered* unique index (`WHERE Isbn IS NOT NULL`), so any number of no-ISBN editions coexist. Add Book page gets a "No ISBN (pre-1974 book)" toggle that swaps the lookup panel for a title/author search hitting Open Library's `/search.json`. Results render as cards with cover, title, author, first publish year, edition count, and an escape link to the full Open Library editions page.

**Surprise** — the schema change was the bigger lift than the feature itself. Filtered unique indexes are well-supported in SQL Server but not all EF tutorials mention them — `.HasFilter("[Isbn] IS NOT NULL")` was the magic. Also: Open Library's search returns *works*, not editions, which constrained the UX (Drew picked option A: show works only, user fills format/print-date manually from the book in hand).

**Lesson** — when an "obviously simple" feature has a hidden schema change, scope it explicitly upfront. Drew's first ask was "title/author lookup, easy right?" — but the real work was "make ISBN optional everywhere it's currently required". Surface that clearly in the plan so the user can see the tradeoff.

**Quotable** — Drew's "actually I might want to capture pre-1974 books, my Christie collection has a few" — that one sentence opened up the whole question of how the model handles non-ISBN identifiers, which we'd previously assumed away.
