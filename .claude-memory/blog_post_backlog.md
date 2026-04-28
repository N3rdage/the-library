---
name: Blog post backlog — corpus catch-up + going-forward queue
description: One-time mine of all retros + patterns.md as of 2026-04-28. Each entry is a candidate post Drew can pick from. Already-shipped posts at the top with their source mapping; catch-up candidates ranked by strength of angle; "skip/fold-in" listed for completeness so the count is definitive. Going-forward, new retros add their candidate post here at write time.
type: project
---

## Status as of 2026-04-28

- **4 posts shipped** (covered).
- **8 strong catch-up candidates** (clear angle, durable lesson, transferable beyond this stack).
- **6 possible catch-up candidates** (could be a post or could fold into another — TBD when picked up).
- **15+ retros with no obvious blog hook on their own** (specific, dated, or already covered — listed at the bottom for the definitive count).

## Already shipped

| # | Date | Title | Drew on |
|---|---|---|---|
| 1 | 2026-04-23 | [Why the most-edited part of our codebase isn't code](../../blog/2026-04-23-most-edited-isnt-code.md) | `patterns.md §3` (memory as durable context); the four memory-type framing |
| 2 | 2026-04-24 | [Why our risky UI rollouts ship as two-line PRs](../../blog/2026-04-24-01-scaffold-first-rollout.md) | Book View page arc retros (`retro_book_view_page_pr1`–`pr4` + `_swap`); the "scaffold opt-in → feature PRs → swap when feature-complete" template |
| 3 | 2026-04-27 | [Empty staging catches schema, not data](../../blog/2026-04-27-01-empty-staging-catches-schema-not-data.md) | `retro_staging_db_separation.md`; the EF-transactional-vs-broken framing |
| 4 | 2026-04-28 | [Why I plan even when you didn't ask](../../blog/2026-04-28-01-why-i-plan-even-when-you-didnt-ask.md) | `patterns.md §1`, `feedback_planning_conventions.md` (load-bearing) + `feedback_plan_prefix.md` (early opt-in version, now vestigial); the *make-the-safe-default-cheap* generalisation — structure dissolves the execute-fast-vs-plan-first trade-off |

## Strong catch-up candidates

Concrete angle, durable lesson, transferable beyond BookTracker's stack. Order is rough — pick whichever speaks to current energy.

- **"I didn't click that button"** — `patterns.md §4` (browser-test honesty) + the bonus pattern "caveat as first-class output." The honest framing: AI compiles, type-checks, unit-tests; AI does *not* click buttons. Standardising "honest caveat — not browser-tested" as a footer is the load-bearing convention. Audience: AI-collaboration; teams worried about over-reliance.

- **"Compiler errors are a refactor todo list"** — `patterns.md §8`, anchored by `retro_work_refactor.md` (30+ files, top-down compile-error sweep) and `retro_pen_names.md`. Why tight types pay back during refactors regardless of who's writing the code. Audience: backend / typed-language devs; resonates beyond AI.

- **"Five PRs, same template, four distinct decisions" — the duplicate-management arc retrospective** — sources: `retro_duplicates_listing` / `_author_merge` / `_work_merge` / `_edition_merge_and_enrichment` / `_book_merge`. The merge-shape template (auto-fill-empties, transactional, refusal vs aggregator) held across five entity types; the per-entity differences were the interesting part. Audience: data-modelling devs; meta-pattern for repeated-feature-shape work.

- **"Having a permission ≠ granting that permission to someone"** — `retro_easy_auth_secret_rotation.md`. The privilege-level distinction generalises: Graph `AppRoleAssignment.ReadWrite.All`, Kubernetes RBAC verbs on Roles vs RoleBindings, SQL `GRANT … WITH GRANT OPTION`, Linux chmod vs chattr. One-paragraph generalisation post; broad audience.

- **"No findings is a valid audit outcome"** — `retro_security_audit.md`. The framing argues *how* each section was verified is the durable artefact, not the verdict. Living-doc + monthly-issue-cadence pattern as the alternative to a point-in-time PDF. Audience: security-curious devs; broader than "audit your code" content.

- **"Try-and-learn beats spec-first for destructive operations"** — `retro_reusable_instructions.md`. Two reusable paste-into-another-Claude prompts (git history rewrite, going-public walkthrough) that emerged from working through the operation rather than specifying it upfront. Reusable-prompts-as-deliverables is the meta-shape. Audience: AI-collaboration; high relevance for the blog's core readers.

- **"Auto-fill-empties beat strict-replace"** — `retro_edition_merge_and_enrichment.md`. The first version was strict-replace; failed the first time it was used. Pivoted to auto-fill-empties + retrofit existing records. The lesson is bigger than dedup: "designed-with-no-data" defaults often pick the wrong semantics. Audience: data-modelling, dedup-curious.

- **"Lookup data is messier than your tests"** — `patterns.md §10`, anchored by `retro_genre_matcher.md` (the bidirectional `Contains()` bug), `retro_format_detection.md`, `retro_imprecise_dates.md`, `retro_trove_fallback.md`. Real-surface examples from Open Library + Google Books + Trove. Budget for "lookup data quality" as ongoing, not one-time. Audience: anyone integrating external metadata APIs.

## Possible catch-up candidates

Could be a post; could fold into one of the strong candidates as a section. TBD when picked up.

- **"Drift across adjacent code is a bug-shaped gap"** — `retro_bug_nest.md`. Three bugs, each with a pre-existing pattern in the codebase that would have prevented it. The lesson: when introducing a pattern, check whether the not-yet-converted code has the bug-shape the pattern fixes. Could fold into a "Convert as we touch" post or stand alone.

- **"List with expandable detail needs three VM collections, not a framework"** — `retro_authors_drilldown.md`. Lazy-load-on-expand, alias rollup, deep-link from a dashboard. The Three-VM-Collections shape is reusable. Niche but specific.

- **"Convert as we touch — pragmatic UI-library migration"** — `retro_mudblazor_pilot_and_theme.md`. Bootstrap and MudBlazor coexist per-page indefinitely by design. No deadline, no big-bang rewrite. Probably folds with the bug-nest "drift" insight.

- **"Find-or-create at every save site"** — `patterns.md §7`. One resolver helper called from five sites; silent auto-create over typeahead-suggest as the friction-vs-rigour trade-off. Would pair well in an "interaction defaults" post.

- **"Additive-then-cutover schema migrations"** — `patterns.md §5`, `retro_work_refactor.md`, `retro_book_to_edition_copy.md`. PR1 (additive, dual-write) then PR2 (cutover, drop). Niche to data-model refactors but a strong case study.

- **"Planning docs uncover preferences silent defaults miss"** — `retro_going_public.md`. Drew picked Option A on `.claude-memory/` over my recommended B because of project framing I didn't have. The lesson is about explicit-options-with-recommendations as a planning shape. Could fold into the plan: prefix post or stand alone.

## Other retros — no obvious blog hook on their own

For the definitive count. Many are valuable as repo memory or as one-paragraph references inside the posts above; few are post-shaped on their own.

| Retro | Why not a standalone post |
|---|---|
| `retro_initial_scaffolding` | Reconstructed from git, dated, mostly setup-narrative |
| `retro_book_data_model_v1` | Superseded by later Edition+Copy and Work refactors |
| `retro_book_to_edition_copy` | Folds into additive-then-cutover post |
| `retro_format_detection` | Folds into "lookup data is messier than your tests" |
| `retro_genre_matcher` | Folds into "lookup data" post |
| `retro_genre_taxonomy` | Domain-modelling; narrow |
| `retro_no_isbn_lookup` | Filtered-unique-index detail; narrow |
| `retro_imprecise_dates` | DateOnly + precision pair; folds into "lookup data" |
| `retro_trove_fallback` | Silent-skip-when-keyless; folds into "lookup data" |
| `retro_chores` | License + small fixes; low signal |
| `retro_pwa` | Niche infra (manifest + icons + SW) |
| `retro_pwa_auth_exclusions` | Specific Easy Auth gotcha; could be a paragraph elsewhere |
| `retro_bulk_capture` | Barcode + photo OCR specifics |
| `retro_series_and_shopping` | 8 PRs in a day; weak narrative arc |
| `retro_library_groupings` | UX accordion + lazy-load; narrow |
| `retro_add_copy_flow` | Duplicate detection at lookup; specific |
| `retro_pen_names` | Domain-modelling for aliases; folds into compile-error post |
| `retro_book_view_page_pr1`–`pr4` + `_swap` | Already covered by post #2 |
| `retro_mvvm_refactor` | .NET-specific; could ground a "test-infra as gateway" post |
| `retro_ai_integration` | Multi-provider arch; dense, would need narrowing |
| `retro_architecture_doc` | Doc discipline; one-paragraph reference at most |
| `retro_dependabot_hygiene` | Dependency hygiene; niche |
| `retro_prologue_claude_desktop` | Pre-code transcript; specific to project genesis |
| `retro_infra_security_arc` | VNet/KV/PE/AI deep dive; would need niche audience |
| `retro_refresh_local_db` | BACPAC script + SqlPackage gotchas; could ground a "scripts as durable artefacts" post |
| `retro_author_merge` | InMemory provider footguns; could ground a test-infra post |

## Going-forward pattern

After this catch-up corpus is mined, new retros add their own backlog entry at write time:

- When a retro is written, add a one-bullet candidate post at the top of "Strong" or "Possible" with the source retro link.
- When a post ships, move its entry from candidates to "Already shipped."
- When the catch-up "Strong" list empties, the going-forward pattern is the only source of new candidates — no more sweeps needed.

## Suggested starter prompt for picking the next post

In a fresh session, this is enough to choose the next one to draft:

> Let's pick the next blog post from the backlog. Suggest 3 from the strong-candidate list with one-line angles and which retro is the anchor.
