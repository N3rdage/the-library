# TODO

Outstanding work items for BookTracker. This is the single source of truth — code-level `// TODO` comments provide local context but this file is the master list.

## Infrastructure

- [ ] Replace migrate-on-startup with deploy-time migration step (`Program.cs:39`) — use `dotnet ef migrations bundle` from GitHub Actions once the app goes multi-instance
- [ ] Proper error handling — structured logging, correlation IDs, user-friendly messages by category, separate 404 handling (`Program.cs:50`, `Error.razor`)
- [ ] GitHub Environment with required reviewers for staging-to-production slot swap (`infra/README.md`)

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

## Planned features

- [ ] AI book recommendations via the Anthropic API
- [ ] Wishlist UI (model exists, no pages yet)
