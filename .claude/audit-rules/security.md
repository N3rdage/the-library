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

## Suppress: AZRE-003 (sub-finding — db_ddladmin grant)

The skill's AZRE-003 rule flags broad database role grants. BookTracker grants both managed identities `db_ddladmin` (in addition to `db_datareader` / `db_datawriter`) on their respective DBs.

**Why:** This grant is required because the app runs `Database.MigrateAsync()` at startup — schema-changing migrations need DDL permissions. A future TODO (`Replace migrate-on-startup with deploy-time migrations`, currently open) would let us drop `db_ddladmin` from the app's runtime identity and shift migrations to a deploy-time identity. Until that lands, the broader grant is the deliberate trade-off documented in `SECURITY-AUDIT.md` §10.

The skill should still report this as "Areas verified clean — accepted trade-off" rather than silently dropping it from the report; the suppression-with-rationale stays visible.

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

## Notes for the audit run

- Cross-reference `SECURITY-AUDIT.md` (the living doc) for areas already documented as clean. Finding drift between the new audit and that doc is a useful signal — either `SECURITY-AUDIT.md` needs updating, or the new finding is real.
- The `audits/` directory is gitignored — reports stay local to the dev machine, which is fine for a public repo where audit reports might surface ongoing soft-spots that we don't want to advertise. Living-doc summary stays in `SECURITY-AUDIT.md`; per-run snapshots stay local.
