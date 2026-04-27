---
name: Project retros — index
description: Short post-mortem notes per shipped feature. Raw material for Drew's blog. New retros land here when a feature merges.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
Short retros are written at merge time and live in this directory. Each one has the same shape:
- **Shipped** — one-line summary of what hit main.
- **Surprise** — what we didn't expect going in.
- **Lesson** — what's worth remembering for next time.
- **Quotable** — optional, a moment worth telling in a blog.

Three cohorts of retros — split by source and shape:

## Prologue (before any code landed)

Pre-commit conversation preserved verbatim, annotated for blog mining. Different shape from the post-feature retros below — no "Shipped" section because nothing had shipped yet; instead a **Minable moments** analysis on top of the raw transcript.

- [Prologue — Claude Desktop conversation](retro_prologue_claude_desktop.md) — 14 April 2026 architecture chat; SQL → Postgres → SQL flip-flop, the directory-write friction that framed the IDE handoff, first exposure to plan mode

## Lived recall (this session, 2026-04-18 onward)

Written at merge time with full conversational context. Richer detail.

- [Infra security arc](retro_infra_security_arc.md) — VNet → KV → SQL PE → AI in eastus2 → docs (5 PRs, ~2 weeks)
- [Format detection + backfill](retro_format_detection.md) — richer BookFormat enum + the marker-table pattern that got reused later
- [Genre matcher saga](retro_genre_matcher.md) — bidirectional `Contains()` was the bug; tighter regex + denylist + second backfill
- [No-ISBN search flow](retro_no_isbn_lookup.md) — filtered unique index + Open Library title/author search
- [Work refactor](retro_work_refactor.md) — additive PR1 + cutover PR2; the seed-migration MERGE fix
- [Pen names](retro_pen_names.md) — self-referential alias entity + canonical rollup; `/authors` page
- [Library groupings](retro_library_groupings.md) — accordion + lazy load per group + canonical rollup again
- [Add-copy + "Add another?"](retro_add_copy_flow.md) — duplicate detection at lookup, not at save
- [Imprecise dates](retro_imprecise_dates.md) — DateOnly + DatePrecision enum, kept sortability
- [Chore arc](retro_chores.md) — license, horror sub-genres, blazor-error-ui CSS bug
- [Trove as third-line lookup](retro_trove_fallback.md) — silent-skip-when-keyless; diagnosed the miss before picking the provider
- [Duplicates — detection + listing](retro_duplicates_listing.md) — PR 1 of dedup series; conservative-then-loosen on first contact; polymorphic dismissal table
- [Duplicates — Author merge](retro_author_merge.md) — PR 2 of dedup series; two InMemory-provider footguns (transactions, pending-change visibility); query-param banners for cross-page messages
- [Infra — refresh-local-db script](retro_refresh_local_db.md) — BACPAC prod → local; SqlPackage `/ua:true` custom-domain gotcha; `finally` pattern earned its keep
- [Duplicates — Work merge + Attach existing Work](retro_work_merge.md) — PR 3 of dedup series; no-surprise clean run thanks to earlier test-infra fixes; the merge-shape template holds
- [Duplicates — Edition merge + auto-fill + covers](retro_edition_merge_and_enrichment.md) — PR 4 of dedup series; strict-replace didn't survive first use → switched to auto-fill-empties (retrofit); cover art retrofit; retrofit-inside-new-feature as a legitimate PR shape
- [Duplicates — Book merge](retro_book_merge.md) — PR 5 of dedup series; aggregator shape → no refusal path; Rating=0 heuristic as UI-semantics bet; series retrospective — five PRs, same template, four distinct per-entity decisions
- [PWA — installable on mobile + desktop](retro_pwa.md) — manifest + icons + service worker; generated PNGs via PowerShell System.Drawing to avoid tool dependencies; stable-vs-versioned URL split for SW
- [PWA — Easy Auth excludedPaths fix](retro_pwa_auth_exclusions.md) — follow-up after install broke; exact-vs-prefix match gotcha; `az webapp auth show` is a V1-shaped legacy CLI that lies about V2 configs — REST is ground truth
- [MudBlazor pilot + warm library theme](retro_mudblazor_pilot_and_theme.md) — two-page pilot + custom palette in the same arc; "convert as we touch" rollout strategy; Bootstrap and MudBlazor coexist per-page indefinitely by design
- [Book View page — PR 1 (read-only scaffold)](retro_book_view_page_pr1.md) — browse-first /books/{id} page with single-Work collapse + multi-Work list; opt-in rollout via a "View (preview)" link so the Library list doesn't reroute yet; verifying `origin/main` against the user's "merged" claim caught a missed Enter key
- [Book View page — PR 2 (inline auto-save)](retro_book_view_page_pr2.md) — editable rating/status/notes/tags with a durable UX rule: silent saves when the value IS the feedback, visible save-state when the user composed the value; CTS+IAsyncDisposable debounce pattern
- [Book View page — PR 3 (modal edits for Book + Work)](retro_book_view_page_pr3.md) — two MudDialog edit surfaces with per-modal VMs; MudAutocomplete find-or-create typeahead for Author; stopPropagation plumbing for MudListItem + inner buttons; "Edit all details" → "Full edit page" relabel as a PR-scope concern
- [Book View page — PR 4 (Edition + Copy modals)](retro_book_view_page_pr4.md) — dual-mode dialog VMs (Add+Edit via IsNew flag); inline delete confirm for copies; cascade edition removal on last-copy delete; real EF InMemory vs SQL Server case-sensitivity footgun caught by a Publisher search test and fixed retroactively for Author search too
- [Book View page — Library swap + arc wrap-up](retro_book_view_page_swap.md) — 2-line PR flipped /books clicks from /edit to the new View page; arc-level reflection on the "scaffold opt-in → feature PRs → swap when feature-complete" rollout template and the five-PR arc it anchored
- [Bug nest — double-click, slot-sticky AI config, shopping NRE](retro_bug_nest.md) — three fixes in one session (#103/#104/#105); durable lesson is that each bug had a pre-existing pattern in the codebase that would have prevented it — drift across adjacent code as a bug-shaped gap to inspect at pattern-introduction time
- [Authors page — MudBlazor rewrite + per-row drill-down](retro_authors_drilldown.md) — /authors rebuilt in MudBlazor with lazy-load-on-expand Works/Books drill-down per row; alias rollup on canonical rows with "as X" attribution chips; deep-link from Home top-10; durable lesson is that "list with expandable detail" needs three VM collections, not a state-management framework
- [Easy Auth client secret rotation](retro_easy_auth_secret_rotation.md) — first TODO off the restructured priority list; scheduled twice-yearly rotation via GitHub Actions using the existing OIDC SP; durable lesson is that "having a permission" and "granting that permission to someone" are two different privilege levels — generalises beyond Graph to Kubernetes RBAC, SQL GRANT WITH GRANT OPTION, filesystems
- [Security audit — living doc + automation](retro_security_audit.md) — full 11-area posture walkthrough; all pass/fixed in one PR. Durable artefacts: SECURITY-AUDIT.md living doc, gitleaks on PR + weekly, monthly GitHub-issue review prompt. Durable lesson: "no findings" is a valid audit outcome worth documenting *how* each section was verified — without that, Pass sections silently regress
- [Going public — planning, README, flip](retro_going_public.md) — 3-PR arc (planning doc + README + actual flip). Drew picked Option A on .claude-memory/ over my recommended B because "this project is an AI-first-development experiment, the workflow files are the point" — which reframed the README from product-forward to experiment-forward. Durable lesson: planning docs with explicit options uncover user preferences that silent defaults miss, and those preferences can change downstream scope
- [Reusable instructions as a session deliverable](retro_reusable_instructions.md) — meta-retro on the two paste-into-another-Claude prompts extracted this session (git history rewrite + going public). Durable lesson: try-and-learn iteration beats spec-first for destructive/unfamiliar operations because the gotchas get baked into the answer, and the extraction step afterward is nearly free when the work generalises. Common structural shape for reusable prompts: boundary → discovery → tools → work → verify → stop points → what-NOT-to-do → durable lesson
- [Separating staging from production — the deploy-stack outage that wasn't](retro_staging_db_separation.md) — PR #129; second SQL DB for staging, slot-sticky `DefaultConnection`, per-slot AAD grant. Surprise was the 6-min apparent outage right after `deploy.ps1` ("Connection reset by peer" during AAD login on both slots), recovered by full Stop/Start (not Restart). Durable lessons: Stop/Start ≠ Restart for App Service auth-cache issues; Bicep redeploys can stack transient state-changing operations even when nothing functional changed; empty staging catches schema syntax but misses every data-shape failure (motivating bacpac-sync follow-up); read the failing log line for ground truth before assuming the most-recently-changed thing broke

## Reconstructed from git (pre-2026-04-18)

Written 2026-04-20 from commit messages alone — no lived recall, "Surprise" / "Lesson" lines marked as inferred. Thinner but enough to hang a blog post on.

- [Initial scaffolding](retro_initial_scaffolding.md) — project zero → first deploy in 4 days; the Razor → Blazor pivot at PR #6
- [Book → Copy v1 model](retro_book_data_model_v1.md) — first book/copy split (PR #4); precursor to all later refactors
- [Hierarchical genre taxonomy](retro_genre_taxonomy.md) — curated 48-genre seed list (PRs #11/#12); pays back into AI features
- [Bulk capture trio](retro_bulk_capture.md) — ISBN entry → barcode → photo OCR (PRs #17, #18, #52)
- [Series + Shopping mode](retro_series_and_shopping.md) — 8 PRs in one day (PRs #34-#41); mobile-first
- [BookCopy → Edition + Copy](retro_book_to_edition_copy.md) — first major data refactor (PR #42); precursor pattern to Work refactor
- [MVVM refactor + first tests](retro_mvvm_refactor.md) — PR #24; testability gateway for the rest of the project
- [AI integration arc](retro_ai_integration.md) — PRs #45-#61; from Anthropic-only to multi-provider with toggle
- [Architecture doc discipline](retro_architecture_doc.md) — PR #50; ARCHITECTURE.md + the convention to keep it updated as feature PRs land
- [Dependabot + dependency hygiene](retro_dependabot_hygiene.md) — PR #19 plus the resulting churn (#21/#23/#25-#29); grouping rule learned from observation
