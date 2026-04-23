---
name: Retro — pen names / Author entity
description: Promoted Author from string to first-class entity; self-referential alias model
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — new `Author` entity with `Name unique` + nullable `CanonicalAuthorId` self-FK. A Bachman novel's `Work.AuthorId` points at Bachman (the book is shown how it was published) but aggregations group by `CanonicalAuthorId ?? Id` so King's "top authors" tally rolls up the Bachman titles. New `/authors` page lists every Author with status (canonical / alias of X) and a "Mark as alias of…" dropdown. Find-or-create is silent on save (Drew picked option A: typing a fresh name auto-creates a canonical Author).

**Surprise** — the design conversation took longer than the code. Two real options (separate `AuthorAlias` table vs self-referential) and several second-order questions: which Author does Work point at, the alias or the canonical? (alias — preserves how the book was actually published). What about Series.Author / WishlistItem.Author? (left as strings, scope creep). Two-hop alias chains? (normalised to root canonical so they can't form). The implementation was mostly mechanical once those were settled.

**Lesson** — for entity-promotion refactors, the hard part is enumerating the read-side aggregation rules upfront (canonical-rollup query in HomeViewModel; the `w.Author.Name == X || w.Author.CanonicalAuthor.Name == X` filter pattern) and having those land in the same PR as the entity change. Otherwise you ship a broken-feeling page.

**Quotable** — the "Stephen King vs Richard Bachman" example became the anchor for every design question. Concrete > abstract when reasoning about identity merging.
