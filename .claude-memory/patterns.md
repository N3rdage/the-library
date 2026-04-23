---
name: Working patterns from BookTracker
description: Recurring conventions that emerged from working on BookTracker with Claude. Raw material for blog posts.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
These aren't rules I was given upfront. They emerged from the way Drew and I worked together over a few weeks of feature work and infra changes. Each one has a story behind it; they're listed here as headlines so Drew can mine them for blog structure.

## 1. Plan-first, prefixed with "plan:"

Drew started prefixing messages with `plan:` when he wanted me to propose without executing. That convention lived in a memory file (`feedback_plan_prefix.md`) so it stuck across sessions. The plan response shape that worked: state the file count up front, list the explicit decisions to land (`A1 / A2 / B1 / B2…`), call out scope-creep risks, then *wait*. Resist the temptation to start writing code "while we discuss". Once Drew picks options, execute without re-litigating.

The reverse also matters: when a request *isn't* prefixed `plan:`, dive in but still narrate the plan in the first message. Then ship.

## 2. Small-PR rhythm + branch hygiene

Every change is its own branch + commit, even tiny ones. Drew pushes & merges; Claude doesn't. After merge, Drew says "merged", Claude pulls main, deletes the local branch, and sits ready. Two memory entries codified this: `feedback_github_push.md` (don't push), `feedback_delete_merged_branch.md` (clean up after).

The discipline pays off in two places: (1) PR diffs stay readable; (2) the conversational rhythm gives Drew a natural "test it in prod, come back if there's a problem" beat between features. The genre-matcher bug and the SQL PE migration data-corruption bug were both caught because of this rhythm.

## 3. Memory as durable context

Three categories of memory served distinct purposes:
- **Feedback rules** (`feedback_*.md`): durable behaviour-shaping ("never push", "ask about mobile priority", "TODO tracking", "delete merged branches"). Loaded in every session.
- **User profile** (`user_drew.md`): solo-dev context, stack, GitHub repo, DNS provider — sets defaults so I don't ask "where do you deploy?" every session.
- **Project progress** (`project_*_progress.md`): in-flight multi-PR work tracking. Created when starting an arc, deleted when the arc ships.

The deleted-when-done discipline mattered. The infra-security progress file got deleted after PR 5 merged; the feature was over, and keeping a stale "in-progress" entry would have been worse than no entry at all.

## 4. Browser-test honesty

For UI changes Claude can compile, type-check, and unit-test, but cannot click. CLAUDE.md tells me to run the dev server for UI work — but in practice the dev server holds the dll lock and Drew's running it anyway. So I started saying "Honest caveat — not browser-tested" at the bottom of every UI-touching PR handoff, with two or three specific things worth verifying. Drew tests in prod after merge and feeds back. Saves us both pretending I clicked a button I didn't.

## 5. Additive-then-cutover migrations (PR1/PR2 split)

For schema refactors that change semantics (Work refactor, pen names), the pattern was:
- **PR 1** — add the new entity / column. Dual-write at every save site via a transitional helper. Reads still come from the old shape. Schema is *additive only*.
- **PR 2** — cut over reads. Drop the old columns. Delete the transitional helper.

Drew preferred this over single-PR even when single-PR would have technically worked. Cleaner history, smaller per-PR risk, dual-write phase doubles as a deploy-time correctness check. The Work refactor's PR1 was the one that actually shipped a data-corruption bug — and because PR2 hadn't yet shipped, the fallback "manually reconstruct from Book.* columns" was real.

## 6. Idempotent data migrations + marker tables

Migrations that touch data have to be safe to re-run. Two patterns:
- `IF NOT EXISTS (...) INSERT` for SQL inside `migrationBuilder.Sql()`.
- `MaintenanceLog` table (`Name unique`, `CompletedAt`, `Notes`) for one-shot startup data backfills (`EditionFormatBackfillService`, `BookGenreBackfillService`). The backfill checks the marker on entry and skips if present.

The marker pattern got reused twice in two PRs. We resisted abstracting it — two instances isn't a pattern, it's a coincidence — but the shape stayed identical and that's fine.

## 7. Find-or-create at every save site (resolver helpers)

When user input maps to an entity (typing an author name → `Author` row), every save site should route through one resolver function (`AuthorResolver.FindOrCreateAsync`). Drew chose option A (silent auto-create on save) over option B (suggest matches as you type) — much less friction, dedup later via a dedicated UI page. The pattern: one helper, called from BookAdd / BookEdit / BulkAdd / Shopping wishlist→library / BookEdit's "add another work". Five sites, one source of truth.

## 8. Type-system as feedback loop

For big refactors (Work cutover, pen names), the workflow was:
1. Change the model.
2. Build.
3. Read the compiler errors as a refactor todo list.
4. Fix top-down.

Worked because the codebase has tight types and few `dynamic` / `object` shortcuts. Each compile error pointed at a specific call site that needed updating. We got through 30+ files of cutover work in the Work refactor PR2 mostly by following the errors, then sweeping back through tests at the end.

## 9. Production-first iteration

Drew tested in prod after every merge. Bugs surfaced fast: the genre matcher false positives, the SQL PE migration partial-state failure, the blazor-error-ui CSS bug, the imprecise-dates need. Each became the next PR. No staging environment, no big-bang testing — small PR, deploy, check, fix in next PR.

This works because (a) Drew is the only user, (b) the small-PR rhythm makes "fix in next PR" cheap. Wouldn't transfer cleanly to a multi-user product, but for a personal app it's strictly better than over-engineering CI gates.

## 10. Lookup data quality is an ongoing target

Open Library + Google Books are *messy*. Real surface-level data we hit:
- Subjects like `"Romance languages"`, `"Open_syllabus_project"`, `"20th century"` (genre matcher).
- Format strings that don't exist (`"Mass Market Paperback"` is sometimes just `"Mass-Market"`) (format normalizer).
- Date strings like `"1934"` or `"c1932"` (date precision).
- Authors that are pseudonyms hidden behind "Stephen King" (pen names — though this is a user-facing model question, not a lookup quality one).

Each piece of upstream noise became its own normalizer + denylist + tests. Worth budgeting for "lookup data quality" as a recurring chunk of work, not a one-time setup.

## Bonus: the conversational style itself

A few smaller patterns that didn't make the headlines but mattered:
- **End-of-turn summary**: 1–2 sentences, what changed and what's next. Always.
- **Caveat as first-class output**: "honest caveat — not browser-tested" became a standard footer for UI work.
- **Q1/Q2/Q3 design questions before code**: forces explicit choices, makes the trade-offs visible, gives Drew an audit trail for why we picked what we did.
- **Bundling related-but-tiny work**: Drew's "Add another?" checkbox got bundled into the duplicate-detection PR rather than spun up as its own. Small bundles are fine when the files overlap; chore-only PRs are fine when they don't.
- **Asking "what's it like in 4 weeks"**: for sizing decisions (pagination, group counts), the right framing is the near-future scale, not today's.
