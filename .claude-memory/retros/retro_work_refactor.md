---
name: Retro — Work refactor (PR1 + PR2)
description: Promoted "the abstract creative unit" above Book; ~50 files touched across two PRs; the seed-migration data-corruption fix
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — new `Work` entity (Title, Subtitle, Author, FirstPublishedDate, Genres, Series), m:m to Book. PR 1 was additive + a `WorkSync.EnsureWork(book)` helper called at every save site so each existing Book got a 1:1 mirroring Work. PR 2 cut over reads, dropped the legacy Book columns, deleted the helper. Compendium support (N Works per Book) lives on the Edit page's "Other works" section.

**Surprise** — the seed migration that filled in Works from existing Books over-matched on a Title/Subtitle/Author/SeriesId join — Drew has multiple printings of the same Christie novel under different ISBNs, so two Books with identical metadata each linked to *both* freshly-inserted Works. Step 3 then tried to insert the same `(WorkId, GenresId)` pair twice. PR1 deployment failed with `PK_GenreWork` violation. The fix was a `MERGE ... OUTPUT` that maps each Book to its freshly-inserted Work 1:1 by construction. EF wraps migrations in transactions so the failure rolled back cleanly — no manual cleanup needed.

**Lesson** — for any data migration that joins on "logical equality" of strings/tuples, ask "what if two source rows have identical values?" before you ship. The MERGE+OUTPUT pattern is the right tool for "insert + capture which inserted ID went with which source row" but I had to look it up — it's not a SQL idiom you reach for daily. Also: Drew chose option B (two PRs) over option A (one PR) explicitly for cleaner history, even though one PR would have been technically sufficient. The boring-but-clean choice paid off when PR1 needed a hotfix mid-deploy.

**Quotable** — the post-failure analysis: "EF wraps each migration in a transaction by default, so prod has no partial state and `__EFMigrationsHistory` doesn't record SeedWorksFromBooks. Redeploying with the fixed body re-runs cleanly." Felt good to be able to say that with confidence rather than panic.
