# TODO

Outstanding work items for BookTracker. Priority-ordered: **security items first** (they always go to the top regardless of size), then the rest sorted by **increasing size / complexity** so small wins are at the top of the non-security list. This file is the single source of truth — code-level `// TODO` comments provide local context but this list is the master.

Size legend: **XS** ≈ one-line fix, **S** ≈ one short session, **M** ≈ a day or a tight multi-PR series, **L** ≈ a multi-day arc or broad cross-cutting work, **XL** ≈ blocked / needs external change.

## Open

| # | Category | Name | Description | Size |
|---|---|---|---|---|
| 1 | Security | GitHub Environment w/ required reviewers for slot swap | Gate the staging→prod slot swap behind a reviewer approval in GitHub Environments (`infra/README.md`). Currently anyone with merge rights can trigger a swap. | S |
| 2 | Security | Security audit (multi-area walkthrough) | Walk the app's security posture post-PWA excludedPaths opening. Covers: Easy Auth `excludedPaths` (confirm only non-sensitive static assets, no Blazor routes starting with `/icons` etc.); CSP headers (`default-src 'self'` baseline + exceptions for html5-qrcode camera, Anthropic/Google Books/Open Library images); SignalR hub auth (`/_blazor/*` gated); KV access paths (managed identity scoping, no raw-secret leaks in config UI); dependency vulnerabilities sweep; PII-in-logs sweep; SQL injection surface (`FromSqlRaw`/`ExecuteSqlRaw` — should be none); JS interop XSS sweep (`IJSRuntime.InvokeAsync` with user-controlled strings); Azure resource RBAC scoping; custom-domain HSTS + HTTPS sanity check. | L |
| 3 | UI / UX | PWA meta-tag deprecation | Add `<meta name="mobile-web-app-capable" content="yes">` alongside `apple-mobile-web-app-capable` in `Components/App.razor`. Chrome warns the Apple-specific one is deprecated; keep both so Safari install-to-home-screen still works. | XS |
| 4 | Duplicates | PR 6 — audit "add Edition by ISBN" gap on Edit Book | Audit first, possibly drop. Partial implementation exists via `BookEditViewModel.NewEditionLookupIsbn`; only open a PR if the audit shows a real gap. | S |
| 5 | Infra | Validate Trove ISBN lookup end-to-end | Once the NLA API key arrives, drop into `appsettings.Development.json`, retry a self-published ISBN the other providers miss (e.g. `9780645840407`), confirm DTO parse matches Trove's live v3 response. Follow-up PR if the shape differs. | S |
| 6 | Series | Context help tips in UI | Explain the difference between a "Series" (numbered, known order like The Ender's Game Saga) and a "Collection" (loose grouping like Discworld / Hercule Poirot). Probably small tooltips or helper text on the series picker. | S |
| 7 | Series | Revisit Collection ordering | Currently defaults to publication order (`Series.cs`). Revisit once enough Collection data is captured to see what the right default actually is. | S |
| 8 | App | Proper error handling | Structured logging, correlation IDs, user-friendly messages by category, separate 404 handling (`Program.cs:50`, `Error.razor`). Security-adjacent — make sure no stack traces or internals leak in prod error responses. | M |
| 9 | AI | Cost tracking | Persistent token/cost logging beyond the session counter. | M |
| 10 | UI / UX | Manage publishers UI | Rename / merge duplicates / delete unused (`Publisher.cs:5`). Mirrors the existing Authors page pattern (drill-down + alias/merge actions). | M |
| 11 | UI / UX | Shelf-order view | Show books in series order (within each series/collection) or first-publish-date order, so the list mirrors the physical shelf. Probably a sort option on the Library page or a dedicated shelf-planner view. | M |
| 12 | AI | AI-assisted genre matching | Use `IAIAssistantService.SuggestGenresAsync` to classify a book against the preset taxonomy, replacing/augmenting the rule-based `FuzzyGenreMatch` in `BulkAddViewModel.SaveBookAsync`. Higher accuracy, ~$0.0003/book on Sonnet, ~1s per book. Useful for genuinely ambiguous subject strings. | M |
| 13 | Series | Multiple authors on anthologies | "The Best Science Fiction of the Year" has different editors per volume. Per-book authors carry the detail for now; use "Various Authors" or leave blank (`Series.cs`). | M |
| 14 | Series | Multi-series membership | A Discworld novel could belong to both "Discworld" and "Discworld: City Watch" sub-series. Currently one series per book (`Series.cs`). | M |
| 15 | Series | API enrichment for series detection | Open Library has series data that could auto-suggest series membership during ISBN lookup (`Series.cs`). | M |
| 16 | UI / UX | UI testing approach | Evaluate bUnit (component-level) and/or Playwright (browser-level) for testing screens and views. Currently ViewModels are well-tested but Razor markup is not. | M |
| 17 | Blog | Initial blog post extract | Pick a first post from the corpus in `.claude-memory/retros/` + `.claude-memory/patterns.md` and draft it. Likely candidates: (a) small-PR rhythm with examples, (b) additive-then-cutover migration using the Work refactor, (c) "memory as durable context" walking through the actual memory directory. See `.claude-memory/project_blog.md`. | M |
| 18 | UI / UX | Accessibility review | Screen reader support, keyboard nav, ARIA labels, colour contrast, focus management. Broad sweep across every page. | L |
| 19 | Data model | Short stories / novellas as first-class entities | Some stories appear standalone *and* inside collected-works books. Requires a Story entity (Title, Author, FirstPublished) + many-to-many to Book. Worth modelling only if you actually want to query "which book contains story X" or "all stories by author Y". **Note:** the Work refactor partially addresses this — Work IS that abstract creative unit, and a Book can contain many Works. Defer until the use case is concrete. | L |
| 20 | Infra | Replace migrate-on-startup with deploy-time migrations | Use `dotnet ef migrations bundle` from GitHub Actions once the app goes multi-instance or needs zero-downtime deploys (`Program.cs:39`). Currently migrate-on-startup is simple + safe for the single-instance App Service. | L |
| 21 | AI / Infra | Re-add Microsoft Foundry (Claude on Azure) | Brings back `claude-sonnet-4-6` + `claude-opus-4-7` deployments, the Foundry Private Endpoint, and the `cognitiveservices.azure.com` DNS zone. **Blocked:** current subscription is Sponsored, which MS excludes from Claude on Foundry. Needs EA / MCA-E migration first. Direct Anthropic API works in the meantime. | XL |

### Notes on Open items

- **Duplicate detection** is conservative (exact-after-normalisation). If the noise/signal ratio is wrong in practice, add fuzzy matching (Levenshtein / token similarity) as a follow-up — don't plumb confidence scores until there's a reason for it.
- **MudBlazor rollout** is ongoing via "convert as we touch" (see `ARCHITECTURE.md` > UI component library). Not a scheduled TODO — each page converts when it's substantively edited for other reasons.

## Shipped

| # | Category | Name | Description | Estimate | Actual |
|---|---|---|---|---|---|
| — | Security | Easy Auth client secret rotation | Scheduled twice-yearly rotation via GitHub Actions using the existing booktracker-ci OIDC SP. Keeps latest 2 passwords alive to cover KV reference refresh. 2-year secret lifetime kept as fail-safe. Granted the CI SP KV Secrets Officer, owner of Library-Patrons, Graph Application.ReadWrite.OwnedBy. | M | 1 PR (#111 — bundled feature + fix for Graph app-role grant permission/error handling) |
| — | UI / UX | MudBlazor pilot + warm library theme | Pilot on Home + MergeBook, custom "leather/brass/parchment/espresso" palette, opt-in rollout across subsequent pages. | M | 2 PRs (#93 pilot+theme, #94 ARCHITECTURE doc) |
| — | UI / UX | Book detail (View) page arc | /books/{id} replaces /edit as the default browsing surface: read-only scaffold → inline auto-save (rating/status/notes/tags) → modal edits for Book + Work → Edition + Copy modals → Library list swap. | L | 6 PRs (#95, #97, #98, #99, #100, + early genre taxonomy #101) |
| — | UI / UX | MudBlazor genre picker | Typeahead + chips + collapsible MudTreeView; wired into WorkEditDialog. Shipped alongside non-fiction taxonomy expansion (Reference/Art/Religion). | M | 2 PRs (#101 taxonomy, #102 picker) |
| — | UI / UX | Authors page drill-down rewrite | MudBlazor rewrite of /authors with per-row Works/Books toggle, alias rollup on canonical rows, deep-link from Home top-10. | M | 2 PRs (#107 rewrite, #109 scroll-into-view polish) |
| — | UI / UX | Mobile UX polish bundle | Hamburger auto-close on nav + clickable Home stat tiles. | S | 1 PR (#106) |
| — | UI / UX | Progressive Web App | Installable on mobile + desktop via manifest + icons + service worker. Network-first-with-cache-fallback for static assets; `/_blazor/*` passes through. Icons reproducible via `scripts/generate-pwa-icons.ps1`. | M | 2 PRs (#91 install, #92 auth-exclusions fix) |
| — | Duplicates | Duplicate management arc (PRs 2–5) | Author merge, Work merge + attach-existing-Work, Edition merge + auto-fill-empties + cover retrofit, Book merge. Detection + listing predates this list. | L | 4 PRs (#87, #88, #89, #90) |
| — | Bugs | Bug nest — double-click, slot-sticky AI, shopping NRE | Dialog double-click guard; Bicep always-emit KV refs + slot-sticky; Shopping ISBN-match NRE (missing `.ThenInclude(w => w.Author)`). | S each | 3 PRs (#103, #104, #105) |
| — | AI | AI book recommendations | Multi-provider (Anthropic direct + Azure OpenAI + Microsoft Foundry in code) with runtime picker. Genre cleanup, collection cataloguing, shopping suggestions, book advisor. | L | ~17 PRs (#45–#61) |
| — | UI / UX | Wishlist UI | Integrated into Shopping page as the shopping-list section. | S | 1 PR |

### Notes on Shipped items

- Priority numbers are omitted for shipped rows — the `#` column is a placeholder. If we start planning shipped work retrospectively (e.g. for post-merge review), priority can go back in.
- The retro for each shipped arc lives under `.claude-memory/retros/` and is the canonical source for surprises/lessons beyond the one-line summary above.
