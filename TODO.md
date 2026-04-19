# TODO

Outstanding work items for BookTracker. This is the single source of truth — code-level `// TODO` comments provide local context but this file is the master list.

## Infrastructure

- [ ] Replace migrate-on-startup with deploy-time migration step (`Program.cs:39`) — use `dotnet ef migrations bundle` from GitHub Actions once the app goes multi-instance
- [ ] Proper error handling — structured logging, correlation IDs, user-friendly messages by category, separate 404 handling (`Program.cs:50`, `Error.razor`)
- [ ] GitHub Environment with required reviewers for staging-to-production slot swap (`infra/README.md`)
- [ ] Re-add Microsoft Foundry (Claude on Azure) once on an EA / MCA-E subscription — Sponsored subscriptions are not eligible. Brings back `claude-sonnet-4-6` + `claude-opus-4-7` deployments, the Foundry Private Endpoint, and the `cognitiveservices.azure.com` DNS zone. Direct Anthropic API works in the meantime.
- [ ] Schedule rotation of the Easy Auth client secret (`infra/deploy.ps1:105`) — currently rotated only when `deploy.ps1` is re-run, with a 2-year expiry. Options: time-triggered Function/Logic App that rotates the secret + writes the new value to KV, or move to a federated credential / certificate-based credential to drop the secret entirely.

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

- [x] AI book recommendations via the Anthropic API — genre cleanup, collection cataloguing, shopping suggestions, book advisor
- [x] Wishlist UI — integrated into Shopping page (shopping list section)
- [ ] AI cost tracking — add persistent token/cost logging beyond the session counter
