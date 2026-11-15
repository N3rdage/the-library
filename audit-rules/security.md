# BookTracker security-audit project rules

Project-level overrides for the `security-audit` skill (`~/.claude/skills/security-audit/`).

This file is loaded after `default-rules.md` and the `web-app-aspnet-azure.md` template — it can:

- **Add** new rules with the project prefix `BOOK-NNN`.
- **Suppress** template / default rules with `## Suppress: <ID>` + a mandatory **Why:** paragraph.
- **Parameterise** rules where the rule documents a parameter.

To populate this file initially, copy from `~/.claude/skills/security-audit/templates/web-app-aspnet-azure.md` (the `AZRE-NNN` rules) and then layer in the suppressions / project rules below. The skill loads the template automatically when it detects the ASP.NET / Azure stack signature, so the duplicate-by-copy is only needed if you want to *override* a templated rule's content (rare). For most projects, leave the templated rules alone and just add suppressions / project-specific rules here.

## Project context

BookTracker is a single-user (Drew) Blazor Server + Azure SQL + Easy Auth app. Scale is hobby-tier (one user, ~3000 books target), production deployment is a single App Service slot pair (`production` / `staging`) with separate DBs per slot, secrets via Key Vault references with managed identity. Full architecture context is in `ARCHITECTURE.md`; the existing `SECURITY-AUDIT.md` is the living-doc record of past audit outcomes.

The security audit skill should treat `SECURITY-AUDIT.md` as cross-reference material — when a finding aligns with an area already documented there, the report should call that out (e.g. "see SECURITY-AUDIT.md §4 for the deferred CSP nonce decision").

---

## BOOK-001 — Migration safety on schema changes

**Category:** infra
**Severity:** high (when violated)

**What to check:**
- Glob `BookTracker.Data/Migrations/*.cs` for migration files.
- For any migration that uses `migrationBuilder.Sql(...)` for data manipulation (not just DDL), verify the operation is idempotent: `IF NOT EXISTS` guards on inserts, `MERGE` patterns for upserts.
- For any migration that drops a non-nullable column or changes a column type, flag for review against the existing `feedback_deployment_safety.md` memory rule: "migrations must retain data; breaking changes need defaults and review tags."
- Check that the most recent migration's `Up` method is paired with a corresponding `Down` method that genuinely reverses it (not just a placeholder).

**How to verify pass:** List the migration files inspected, the data-manipulation patterns used (or "DDL-only — N/A"), and confirm idempotency where applicable.

**Fix guidance:** For non-idempotent data SQL, wrap in `IF NOT EXISTS` or use the existing `MaintenanceLog` marker-table pattern (see `EditionFormatBackfillService` for the template). For breaking schema changes, add a review tag in the PR title and ensure the change has data-preserving defaults.

**References:** Project memory `feedback_deployment_safety.md`; pattern docs `patterns.md §6` (Idempotent data migrations + marker tables).

---

## BOOK-002 — Empty-staging migration risk

**Category:** infra
**Severity:** medium

**What to check:**
- The current staging DB starts empty (per `retro_staging_db_separation.md`). Migrations that succeed against empty data may fail against prod data.
- For any new migration added since the last audit run: flag if it includes any of the following constraint operations, since these can fail differently on empty vs populated DBs:
  - `ALTER COLUMN ... NOT NULL`
  - Adding a unique index or constraint
  - Adding a FK
  - Adding a CHECK constraint
  - `ALTER COLUMN ... <new-type>` (especially numeric → narrower numeric, string → typed)
- Also flag any `ALTER TABLE` that would rebuild a large table (could time-out on prod's data, even if instant on empty staging).
- Cross-reference: TODO #1 (Bacpac sync from prod → staging) is the durable mitigation. Until that ships, the audit can flag the risk per migration.

**How to verify pass:** List migrations added since last audit; for each, classify as "schema-only safe" / "data-shape sensitive — bacpac-validate before prod" / "no new migrations."

**Fix guidance:** Run a bacpac sync from prod to staging before merging migrations in the data-shape-sensitive category, then validate the migration applies cleanly. Long-term fix is TODO #1 (automate the bacpac sync).

**References:** Blog post `2026-04-27-01-empty-staging-catches-schema-not-data.md`; retro `retro_staging_db_separation.md`.

---

## BOOK-003 — `.claude-memory/` content review for public exposure

**Category:** secrets-management
**Severity:** medium

**What to check:**
- The repo is public (per `retro_going_public.md`) and `.claude-memory/` is tracked deliberately (Option A from the going-public planning doc).
- Glob `.claude-memory/**/*.md` and grep for: email addresses, IP addresses, full names of people other than Drew, internal-feeling identifiers (slack channel names, Linear ticket IDs from previous employers, etc.).
- Specifically check for: real GUIDs in retros (subscription IDs, tenant IDs — should already be redacted), internal hostnames, paths under `/Users/<colleague>/`.

**How to verify pass:** List the categories grepped for and confirm no findings. Note that famous-author names (used as examples) are fine; colleague / client / friend names are not.

**Fix guidance:** Redact in place. For prior-tenant GUIDs, replace with `<tenant-guid>` placeholder. For names, replace with role descriptors or anonymous handles.

**References:** `retro_going_public.md` for the public-flip decision context.

---

> BOOK-004..BOOK-009 codify six findings from the 2026-11-14 run that sat outside the rule set. The id-to-finding mapping is the autonomous agent's best reading of Drew's report; Drew to confirm or correct.

## BOOK-004 — Pull-request workflows hold no cloud credential

**Category:** ci-cd
**Severity:** high

**What to check:**
- `infra/setup-github-oidc.ps1` creates no federated credential with a `pull_request` subject.
- No workflow triggered by `pull_request` grants `id-token: write`.
- `azure/login` appears only in workflows triggered by `push`, `workflow_dispatch` or `schedule`.

**How to verify pass:** List each workflow with its triggers and whether it requests `id-token: write` or runs `azure/login`; confirm none is PR-triggered. Confirm the OIDC script's credential subjects.

**Fix guidance:** Drop the `pull_request` subject from the OIDC script (and from the Entra app registration), and move any Azure step out of PR-triggered jobs.

**References:** `SECURITY-AUDIT.md` §5, §13; the-library #465.

---

## BOOK-005 — Actions pinned to commit SHAs

**Category:** ci-cd
**Severity:** medium

**What to check:**
- Every `uses:` in `.github/workflows/*.yml` references a 40-hex commit SHA, followed by a `# vN` comment naming the version.
- `.github/dependabot.yml` has a `github-actions` ecosystem entry, so pins keep moving.

**How to verify pass:** Grep `uses:` across the workflows; report any line not matching `@[0-9a-f]{40}`. Confirm the Dependabot entry.

**Fix guidance:** Replace the tag with the SHA it resolves to and keep the tag as a trailing comment.

**References:** `SECURITY-AUDIT.md` §13; the-library #465.

---

## BOOK-006 — Local compose binds loopback only

**Category:** infra
**Severity:** medium

**What to check:**
- Every `ports:` entry in `docker-compose.yml` has the form `127.0.0.1:<host>:<container>`.
- Reason: `infra/refresh-local-db.ps1` restores a copy of prod into the local SQL container, so that container holds real data.

**How to verify pass:** List each service's `ports:` entries and confirm the loopback prefix on all of them.

**Fix guidance:** Prefix the mapping with `127.0.0.1:`.

**References:** the-library #465.

---

## BOOK-007 — HSTS at least six months with includeSubDomains

**Category:** web
**Severity:** low

**What to check:**
- `AddHsts` is configured with `MaxAge` of at least 180 days and `IncludeSubDomains = true`.
- `UseHsts` runs outside Development only.

**How to verify pass:** Quote the `AddHsts` options and the `UseHsts` call site in `BookTracker.Web/ProgramSetup.cs`.

**Fix guidance:** Add `services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });`.

**References:** `SECURITY-AUDIT.md` §11; LIB-004.

---

## BOOK-008 — Fetched remote content has a type allowlist and a size cap

**Category:** web
**Severity:** low (medium if the app gains more users)

**What to check:** any code that downloads a URL stored in the database or supplied by a provider (the cover mirror, any future image fetch):
- checks `Content-Type` against a raster-image allowlist before reading the body;
- caps the bytes read from the stream;
- never stores non-raster content;
- validates the URL (https only, no private or loopback addresses).

**How to verify pass:** List each fetch site and confirm all four checks.

**Fix guidance:** Read headers first (`HttpCompletionOption.ResponseHeadersRead`), reject on type or `Content-Length`, read through a bounded stream, drop anything that fails to decode, and validate the URL before the request.

**References:** `SECURITY-AUDIT.md` §12; LIB-006, LIB-017.

---

## BOOK-009 — Workflows that share a mutable cloud resource share a concurrency group

**Category:** ci-cd
**Severity:** low

**What to check:**
- `deploy.yml` and `swap.yml` both toggle the SQL server's public access, so they declare the same `concurrency.group`.
- Any new workflow that touches the same resource joins that group.

**How to verify pass:** Quote the `concurrency:` block of each workflow that touches the SQL server's network settings.

**Fix guidance:** Add the shared `concurrency.group` (currently `sql-firewall-window`) with `cancel-in-progress: false`.

**References:** `SECURITY-AUDIT.md` §13; the-library #465.

---

## Notes for the audit run

- Cross-reference `SECURITY-AUDIT.md` (the living doc) for areas already documented as clean. Finding drift between the new audit and that doc is a useful signal — either `SECURITY-AUDIT.md` needs updating, or the new finding is real.
- The `audits/` directory is gitignored — reports stay local to the dev machine, which is fine for a public repo where audit reports might surface ongoing soft-spots that we don't want to advertise. Living-doc summary stays in `SECURITY-AUDIT.md`; per-run snapshots stay local.
