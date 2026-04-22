# TODO

Outstanding work items for BookTracker. This is the single source of truth — code-level `// TODO` comments provide local context but this file is the master list.

## Infrastructure

- [ ] Replace migrate-on-startup with deploy-time migration step (`Program.cs:39`) — use `dotnet ef migrations bundle` from GitHub Actions once the app goes multi-instance
- [ ] Proper error handling — structured logging, correlation IDs, user-friendly messages by category, separate 404 handling (`Program.cs:50`, `Error.razor`)
- [ ] GitHub Environment with required reviewers for staging-to-production slot swap (`infra/README.md`)
- [ ] Re-add Microsoft Foundry (Claude on Azure) once on an EA / MCA-E subscription — Sponsored subscriptions are not eligible. Brings back `claude-sonnet-4-6` + `claude-opus-4-7` deployments, the Foundry Private Endpoint, and the `cognitiveservices.azure.com` DNS zone. Direct Anthropic API works in the meantime.
- [ ] Schedule rotation of the Easy Auth client secret (`infra/deploy.ps1:105`) — currently rotated only when `deploy.ps1` is re-run, with a 2-year expiry. Options: time-triggered Function/Logic App that rotates the secret + writes the new value to KV, or move to a federated credential / certificate-based credential to drop the secret entirely.
- [ ] Validate Trove ISBN lookup end-to-end once the NLA API key arrives — drop key into `appsettings.Development.json`, retry a self-published ISBN the other providers miss (e.g. `9780645840407`), and confirm the DTO parse matches Trove's live v3 response. Follow-up PR if the shape differs from the one coded against.
- [ ] **Security audit** — walk the app's security posture now that the PWA exclusions opened up specific paths publicly. Areas to cover:
  - Easy Auth `excludedPaths` — confirm only non-sensitive static assets are listed; ensure no Blazor routes or API endpoints accidentally start with `/icons` / `/manifest.webmanifest` / `/service-worker.js`.
  - Content-Security-Policy headers — currently none; consider `default-src 'self'` baseline + exceptions for html5-qrcode (camera), Anthropic/Google Books (images), Open Library (covers). Tighten XSS surface.
  - SignalR hub authentication — confirm `/_blazor/*` is gated by Easy Auth and not accidentally covered by any excluded path.
  - Key Vault access paths — managed identity scoping; verify no app setting leaks raw secrets in App Service config UI.
  - Dependency vulnerabilities — Dependabot catches new CVEs on PR, but sweep for existing once.
  - PII in logs — search `logger.Log*` calls; confirm no book titles / user identifiers in warn/error messages beyond what's needed for diagnostics.
  - SQL injection surface — EF LINQ is safe by construction; sweep for any `FromSqlRaw` / `ExecuteSqlRaw` (there shouldn't be any yet).
  - JS interop XSS — sweep `IJSRuntime.InvokeAsync` calls for user-controlled strings passed to `eval`-equivalent patterns.
  - Azure resource RBAC — confirm App Service identity has only the roles it needs (KV Secrets User, SQL db_datareader/writer/ddladmin, Cognitive Services User on OpenAI); nothing broader.
  - Custom-domain HSTS + HTTPS redirect — already on via `app.UseHsts()` + `app.UseHttpsRedirection()`, sanity-check the headers in prod.

## UI / UX

- [x] **Progressive Web App** (shipped): installable on mobile + desktop via manifest + icons + service worker. Network-first-with-cache-fallback for static assets; `/_blazor/*` passes through. Icons reproducible via `scripts/generate-pwa-icons.ps1`.
- [ ] Evaluate Blazor component library (MudBlazor, Radzen, FluentUI) to replace hand-rolled Bootstrap (`Program.cs:9`) — **next up: pilot in MudBlazor on `/duplicates/merge/book` + `/` Home**, decide from there
- [ ] Manage publishers UI — rename/merge duplicates, delete unused (`Publisher.cs:5`)
- [ ] PWA meta-tag deprecation — add `<meta name="mobile-web-app-capable" content="yes">` alongside the existing `apple-mobile-web-app-capable` in `Components/App.razor` (Chrome dev console warns that the Apple-specific tag is deprecated; keep both so Safari install-to-home-screen still works). Low priority, no functional impact.
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

### Duplicate management (multi-PR series)

Detection + `/duplicates` listing shipped in PR 1 (this branch). Remaining PRs in the series:

- [x] **PR 2 — Author merge** (shipped): side-by-side review at `/duplicates/merge/author/{a}/{b}`, reassigns works + aliases, auto-promotes winner to canonical when winner was an alias of loser, refuses when the two resolve to different canonicals.
- [x] **PR 3 — Work merge + Add-Work-to-Book via search** (shipped): `/duplicates/merge/work/{a}/{b}` transactional merge with "Book contains both" handling and surfacing in preview + success banner. Edit Book page gains an "Attach existing Work" typeahead row above the create-new row. No field auto-enrichment — user copies anything they want to keep (subtitle, series, genres) manually before confirming.
- [x] **PR 4 — Edition merge + auto-fill-empties + cover art retrofit** (shipped): `/duplicates/merge/edition/{a}/{b}` with cover thumbnails. Author and Work merge pages also gained cover art (first-1:1-Book-then-fallback pick). Merge semantics switched from strict-replace to auto-fill-empties across Work and Edition merges (Author has nothing to enrich): any empty winner field gets taken from loser, paired fields move together (date+precision, series+order), genres union. Preview shows exactly what will move.
- [x] **PR 5 — Book merge** (shipped): `/duplicates/merge/book/{a}/{b}` transactional merge. Reassigns Editions (carrying Copies), unions Works + Tags with dedup, auto-fills empty winner fields (Notes, Cover, Rating where 0 == unrated). No incompatibility path — Book is the aggregator. Any residual Edition duplicates (no-ISBN same-format-publisher-date) surface on /duplicates for separate cleanup.
- [ ] **PR 6 (audit first, possibly drop)** — gap-fill on Edit Book's "add Edition by ISBN". Partial implementation already exists via `BookEditViewModel.NewEditionLookupIsbn`; only open a PR if the audit shows a real gap.

Detection is conservative (exact-after-normalisation). If the noise/signal ratio is wrong in practice, add fuzzy matching (Levenshtein / token similarity) as a follow-up — keep the confidence score plumbing absent until there's a reason for it.

## Planned features

- [x] AI book recommendations via the Anthropic API — genre cleanup, collection cataloguing, shopping suggestions, book advisor
- [x] Wishlist UI — integrated into Shopping page (shopping list section)
- [ ] AI cost tracking — add persistent token/cost logging beyond the session counter
- [ ] AI-assisted genre matching — use `IAIAssistantService.SuggestGenresAsync` to classify a book against the preset taxonomy, replacing/augmenting the rule-based `FuzzyGenreMatch` in `BulkAddViewModel.SaveBookAsync`. Higher accuracy, ~$0.0003/book on Sonnet, ~1s per book. Useful for genuinely ambiguous subject strings the rule-based matcher can't disambiguate.

## Blog

- [ ] Initial blog post extract — pick a first post from the corpus in `.claude-memory/retros/` + `.claude-memory/patterns.md` and draft it. Likely candidates: (a) the small-PR rhythm with concrete examples, (b) the additive-then-cutover migration pattern using the Work refactor as anchor, or (c) "memory as durable context" walking through the actual memory directory. See `.claude-memory/project_blog.md` for the brief and recommended workflow.
