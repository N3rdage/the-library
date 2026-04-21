# TODO

Outstanding work items for BookTracker. This is the single source of truth — code-level `// TODO` comments provide local context but this file is the master list.

## Infrastructure

- [ ] Replace migrate-on-startup with deploy-time migration step (`Program.cs:39`) — use `dotnet ef migrations bundle` from GitHub Actions once the app goes multi-instance
- [ ] Proper error handling — structured logging, correlation IDs, user-friendly messages by category, separate 404 handling (`Program.cs:50`, `Error.razor`)
- [ ] GitHub Environment with required reviewers for staging-to-production slot swap (`infra/README.md`)
- [ ] Re-add Microsoft Foundry (Claude on Azure) once on an EA / MCA-E subscription — Sponsored subscriptions are not eligible. Brings back `claude-sonnet-4-6` + `claude-opus-4-7` deployments, the Foundry Private Endpoint, and the `cognitiveservices.azure.com` DNS zone. Direct Anthropic API works in the meantime.
- [ ] Schedule rotation of the Easy Auth client secret (`infra/deploy.ps1:105`) — currently rotated only when `deploy.ps1` is re-run, with a 2-year expiry. Options: time-triggered Function/Logic App that rotates the secret + writes the new value to KV, or move to a federated credential / certificate-based credential to drop the secret entirely.
- [ ] Validate Trove ISBN lookup end-to-end once the NLA API key arrives — drop key into `appsettings.Development.json`, retry a self-published ISBN the other providers miss (e.g. `9780645840407`), and confirm the DTO parse matches Trove's live v3 response. Follow-up PR if the shape differs from the one coded against.

## UI / UX

- [ ] Evaluate Blazor component library (MudBlazor, Radzen, FluentUI) to replace hand-rolled Bootstrap (`Program.cs:9`)
- [ ] Manage publishers UI — rename/merge duplicates, delete unused (`Publisher.cs:5`)
- [ ] Accessibility review — screen reader support, keyboard nav, ARIA labels, colour contrast, focus management
- [ ] UI testing approach — evaluate bUnit (component-level) and/or Playwright (browser-level) for testing screens and views

## Series / Collections

- [ ] Revisit Collection ordering once more data is captured — currently defaults to publication order (`Series.cs`)
- [ ] Investigate multiple authors on Series for anthology collections — e.g. "The Best Science Fiction of the Year" has different editors. Per-book authors carry the detail for now; use "Various Authors" or leave blank (`Series.cs`)
- [ ] Investigate multi-series membership — a book like a Discworld novel could belong to both "Discworld" and "Discworld: City Watch" sub-series. Currently one series per book (`Series.cs`)
- [ ] API enrichment for series detection — Open Library has series data that could auto-suggest series membership during ISBN lookup (`Series.cs`)
- [ ] Context help tips in the UI explaining the difference between a "Series" (numbered, known order like The Ender's Game Saga) and a "Collection" (loose grouping like Discworld or Hercule Poirot)
- [ ] Shelf-order view — show books in either series order (within each series/collection) or first-publish-date order, so the list mirrors the order you'd want them on the physical shelf. Probably a sort option on the Library page or a dedicated shelf-planner view.

## Data model — open questions

- [ ] Decide whether to track short stories / novellas as first-class entities — some stories appear standalone *and* inside collected-works books (e.g. Christie short stories republished in anthologies). Requires a Story entity (Title, Author, FirstPublished) and many-to-many to Book. Worth the modelling effort only if you actually want to query "which book contains story X" or "all stories by author Y including those bundled in collections". Defer until the use case is concrete. **Note:** the Work refactor partially addresses this — a Work is exactly that abstract creative unit, and a Book can contain many Works. Splitting a compendium into per-story Works via the Edit page covers most of the use case.

## Data model — known follow-ups

- [ ] Merge / dedup duplicate Authors — UI on `/authors` to combine two canonical Author rows into one (e.g. "J. R. R. Tolkien" and "JRR Tolkien" entered separately). Reassign all `Work.AuthorId` from the loser to the winner, reassign any aliases, then delete the loser row.
- [ ] Merge / dedup duplicate Works — when the same story has been entered as two separate Works (because two compendiums spelt the title slightly differently). UI to pick a winner, reassign all `BookWork` join rows to the winner, reassign `Work.SeriesId` / `Genres` if the loser had values the winner didn't, then delete the loser row. Surface from the Authors page as well — "Tolkien has 2 Works titled 'The Hobbit', merge?".

## Planned features

- [x] AI book recommendations via the Anthropic API — genre cleanup, collection cataloguing, shopping suggestions, book advisor
- [x] Wishlist UI — integrated into Shopping page (shopping list section)
- [ ] AI cost tracking — add persistent token/cost logging beyond the session counter
- [ ] AI-assisted genre matching — use `IAIAssistantService.SuggestGenresAsync` to classify a book against the preset taxonomy, replacing/augmenting the rule-based `FuzzyGenreMatch` in `BulkAddViewModel.SaveBookAsync`. Higher accuracy, ~$0.0003/book on Sonnet, ~1s per book. Useful for genuinely ambiguous subject strings the rule-based matcher can't disambiguate.

## Blog

- [ ] Initial blog post extract — pick a first post from the corpus in `.claude-memory/retros/` + `.claude-memory/patterns.md` and draft it. Likely candidates: (a) the small-PR rhythm with concrete examples, (b) the additive-then-cutover migration pattern using the Work refactor as anchor, or (c) "memory as durable context" walking through the actual memory directory. See `.claude-memory/project_blog.md` for the brief and recommended workflow.
