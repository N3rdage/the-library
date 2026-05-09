# BookTracker scale-audit project rules

Project-level overrides for the `scale-audit` skill (`~/.claude/skills/scale-audit/`).

This file is loaded after `default-rules.md` and the `web-app-aspnet-azure.md` template — it can:

- **Add** new rules with the project prefix `BOOK-S<NNN>`.
- **Suppress** template / default rules with `## Suppress: <ID>` + a mandatory **Why:** paragraph.
- **Parameterise** rules where the rule documents a parameter (especially `SCALE-004` hot-tables, `SCALE-005` context-per-method threshold, `AZSC-005` Virtualize threshold).

## Project context

BookTracker is a single-user (Drew) Blazor Server + Azure SQL + Easy Auth app. Stated scale target per the project's `feedback_performance_target.md` memory: **3000+ copies**, with informal budgets of **page-load p95 < 1s warm-pool** and **working-set < 300MB peak**. Production deployment is a single App Service slot pair (`production` / `staging`) with separate Azure SQL DBs per slot.

Architecture context: `ARCHITECTURE.md`. Audit-skill family context: `docs/audit-skills.md`. Live security companion doc: `SECURITY-AUDIT.md`. Most relevant cross-reference for *runtime* perf measurement (which this skill explicitly does NOT do): the [May 8 perf-investigation blog post](../blog/2026-05-08-01-i-blamed-the-cold-start-the-trace-disagreed.md) + the two runbook memory entries from that incident (`runbook_sqlclient_pool_zombies.md`, `runbook_container_warmup_calibration.md`).

The first scale audit (`audits/scale-2026-05-08.md`) ran without any project rules — five findings (1 high, 3 medium, 1 low) calibrate this file. Suppressions and parameters here reflect *the actual deferrals captured during that pilot triage*, not theoretical concessions.

---

## Parameter: SCALE-004.hot-tables = ["Books", "Editions", "Copies", "Works", "WorkAuthors", "Authors", "Tags"]

The seven core entity tables that grow with user activity at the 3000+ books target. `Books` and `Editions` are the headline tables (one row per captured book / printing). `Copies` is the physical-copy table (multi-copies-per-edition for collectors). `Works` is the abstract-creative-unit table (compendiums multiply this — a 12-story collection gives 12 Work rows for one Book). `WorkAuthors` is the M:N join carrying per-Work co-authorship + ordering (multi-author works multiply this). `Authors` is bounded by distinct-author count (likely 300-500 at target). `Tags` is folksonomy — could plausibly hit 100-200 at scale though Drew's usage pattern keeps it smaller.

Tables NOT in the hot list — these are bounded by domain:

- `Genres` — fixed taxonomy with reseed-only growth (~50 rows).
- `Series` — manually-curated series rows (Drew has 7 at time of writing; even at scale unlikely to exceed ~100).
- `Publishers` — bounded by distinct publisher set; few hundred at most.
- `IgnoredDuplicates` — bounded by duplicate-pair count.
- `MaintenanceLog` — append-only, but only by infrastructure code, no user growth.
- `WishlistItems` — wishlist size; user-bounded.

---

## Parameter: SCALE-005.max-contexts-per-method = 3

Default kept. The BookTracker pattern is "one method, one context, one logical operation" via `IDbContextFactory.CreateDbContextAsync`. Per-method count of 1 is typical; high per-VM totals come from many distinct user-action methods, not from any single method opening multiple. The 3-per-method threshold should never trigger in this codebase under normal patterns; a future regression to "many sequential opens per method" is exactly what the rule should catch.

---

## Parameter: AZSC-005.virtualize-threshold = 100

Default kept. Below 100 rows the Razor render-tree cost of plain `@foreach` is fine. `/series` and `/publishers` currently render every row without virtualisation but stay well below the threshold (Series ~7, Publishers small) — the rule correctly classifies them as *no applicable surface yet* rather than findings.

---

## Suppress: AZSC-006

The skill's AZSC-006 rule flags any direct write to `Edition.CoverUrl` / `Book.DefaultCoverArtUrl` that doesn't route through the `IBookCoverStorage` abstraction. BookTracker writes the upstream URL at save time and defers blob mirroring to the `CoverMirrorBackgroundService` 30s tick.

**Why:** This is the deliberate architecture documented in `CLAUDE.md` "Cover storage" section: *"there's no save-site integration in PR1 (PR2 will move new-cover mirroring inline at the save site so the polling becomes backfill-only)"*. The save-site direct-URL pattern is intentional and lets the user see a cover render immediately (against upstream URL) while the background service mirrors to blob asynchronously. The trade-off is a brief window where rendering depends on upstream uptime — acceptable for a hobby-tier app, mitigated by the eventual mirror.

**Re-evaluate when** the "PR2 inline mirroring" follow-up referenced in `CLAUDE.md` lands. At that point the save sites should route through the storage abstraction directly and this suppression should be removed.

The skill should still report this as "Areas verified clean — accepted trade-off" rather than silently dropping it from the report; the suppression-with-rationale stays visible across runs.

---

## Suppress: SCALE-009 — `string.Contains` typeahead patterns on Tags table

The SCALE-009 rule flags `Tags.Where(t => t.Name.Contains(q))` in `BookDetailViewModel.SearchTagsAsync` (`BookDetailViewModel.cs:298`).

**Why:** Tags is a folksonomy bounded by user typing patterns. Even at the 3000+ books scale target, expected Tag count is probably 100-200 distinct names. SQL `LIKE '%q%'` against a few hundred rows is single-digit milliseconds — well below a perceptible UX threshold for typeahead. Adding a full-text index would be over-engineering for a single-user hobby app at this scale.

**Re-evaluate when** Tag count crosses ~500 OR Drew measures the typeahead path as slow in App Insights (Q1 query — `dependencies` filtered to the Tags table by `data` field).

The Author and Work typeahead patterns (`MudAuthorPicker.razor:99`, `WorkSearchService.cs:52`) are *not* suppressed — those tables grow more aggressively at scale (Works multiplies with compendiums; Authors is bounded by distinct names but the count grows linearly with new books). Those stay as findings until a future fix or a reasoned suppression.

---

## BOOK-S001 — Cover storage architectural drift

**Category:** code
**Severity:** medium (when violated)

**What to check:**
- The deliberate "save upstream URL → background-mirror" pattern (suppressed under AZSC-006 above) assumes `CoverMirrorBackgroundService` actually runs and mirrors. This rule guards the *opposite* drift: a save site that mirrors inline (direct `IBookCoverStorage.UploadAsync` call at save time) without removing the AZSC-006 suppression.
- Glob `**/*ViewModel.cs` and `**/*Service.cs` for `IBookCoverStorage.UploadAsync` calls. Read each call site.
- Save-time inline upload is the *intended* eventual state per the CLAUDE.md "PR2" follow-up — when it lands, AZSC-006 suppression should be removed in the same PR. This rule flags drift where one of the save sites moves to inline-upload while others stay deferred (split state where some covers mirror inline, others go through the background service).

**How to verify pass:** List `IBookCoverStorage.UploadAsync` call sites; confirm either ALL save-time covers route through it (AZSC-006 suppression should be removed) OR none do (current state, AZSC-006 suppression valid). Mixed state is a finding.

**Fix guidance:** Either complete the migration (move all save-site covers to inline upload, remove AZSC-006 suppression in this file) or revert the partial migration (background service handles all). Don't ship a mixed state without an explicit transition-PR justification.

**False-positive shapes:**
- The `EditionCoverUploadDialog` user-supplied-cover path (`BookDetailViewModel.UploadEditionCoverAsync`) is *legitimately* save-time inline — user uploads a file via dialog, the dialog calls `coverStorage.UploadAsync` directly, the resulting blob URL goes onto the Edition. This is NOT the same surface as the upstream-API-cover mirror path; it doesn't represent drift from the deferred-mirror pattern.

**References:** `CLAUDE.md` "Cover storage" section.

---

## Notes for the audit run

- Cross-reference the May 8 perf-investigation blog post + the two runbook memory entries for runtime context. Findings flagged here without corresponding App Insights signal are still valid but lower-priority — they're patterns waiting for scale.
- The `audits/` directory is gitignored — reports stay local. Living-doc summary stays in `SECURITY-AUDIT.md` (security) — there's no equivalent `SCALE-AUDIT.md` yet; one will be worth creating if the scale-audit run cadence settles into something that needs cross-run summary.
- Re-evaluate this file when:
  - The "PR2 inline cover mirroring" lands (retires AZSC-006 suppression + BOOK-S001 watcher).
  - Tag count or typeahead behaviour changes the SCALE-009 Tags-suppression rationale.
  - Capture pace meaningfully advances toward the 3000+ books target — at which point parameter values may need revisiting.
