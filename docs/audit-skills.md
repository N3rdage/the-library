# Audit skills

BookTracker uses a family of read-only Claude Code "audit skills" — automated code reviews that flag patterns of interest in specific domains. Each skill loads its rule set, scans the project, and writes a dated Markdown report to `audits/`.

## The static-pattern boundary

**All audit skills in this family are static-pattern analysis. None of them measure runtime behaviour.**

This is a deliberate boundary, surfaced because confusing the two leads to using the wrong tool for the job. Static analysis catches *patterns that correlate with problems* — an unbounded `ToListAsync`, a missing `@secure()` decorator, a multi-collection EF Include without `AsSplitQuery`. It catches the *shape*, not the *behaviour*.

For runtime measurement — actual query latency, working-set memory under load, cold-start time, request-pipeline gaps — the source of truth is **Application Insights**. The five reusable KQL queries documented in [the May 8 perf-investigation blog post](../blog/2026-05-08-01-i-blamed-the-cold-start-the-trace-disagreed.md) are the durable artefact for that:

- **Q1** — Top SQL operations by p95 latency (catches `InternalOpenAsync` cold-handshake patterns)
- **Q2** — N+1 detector (SQL calls per Blazor operation)
- **Q3** — End-to-end trace for one slow operation
- **Q4** — Initial page-load durations (true GETs, before SignalR takes over)
- **Q5** — Cold-start sanity check (request p95 over time, sawtooth detection)

Plus the runbook memory entries from the same incident:
- `runbook_sqlclient_pool_zombies.md` — when both slots wedge
- `runbook_container_warmup_calibration.md` — `WEBSITES_CONTAINER_START_TIME_LIMIT` calibration

The audit skills and the App Insights queries are **complementary, not redundant.** The audit catches "this pattern *might* bite at scale." App Insights catches "this pattern *is* biting now." A finding that the audit flags but App Insights shows no signal for is still real but lower-priority — it's a pattern waiting for scale to expose. A signal in App Insights that the audit didn't flag is a rule gap, worth promoting back into the relevant skill.

## The skills

| Skill | Status | Domain | Default rule prefix |
|---|---|---|---|
| `security-audit` | shipped | code, infra (Bicep), CI/CD, secrets, dependencies, runtime, auth | `SEC-NNN` (cross-stack) + `AZRE-NNN` (ASP.NET / Azure stack) |
| `scale-audit` | shipped | data-access (EF / SQL), runtime config, Razor / component rendering, background services | `SCALE-NNN` (cross-stack) + `AZSC-NNN` (ASP.NET / Azure stack) |
| `a11y-audit` | planned (TODO #5) | WCAG form labels, contrast, touch targets, responsive breakpoints | TBD |
| `codehealth-audit` | planned (TODO #6) | dead code, TODO density, test-coverage gaps, ARCHITECTURE.md drift | TBD |

Each skill lives at `~/.claude/skills/<name>/` (user-global, not in this repo) and is triggered by `/<name>` or natural-language phrases ("security audit", "scale audit", etc.). The skill files are kept user-global so they're reusable across projects; project-specific rule overrides live at `<project>/audit-rules/<name>.md` in the repo.

## Project-specific overrides

Each skill loads, in order: default rules → stack-specific template → project rules. The project file (e.g. `audit-rules/security.md`, `audit-rules/scale.md`) can:

- **Add** new rules with a project prefix (e.g. `BOOK-001` for security, `BOOK-S001` for scale).
- **Suppress** template / default rules with `## Suppress: <ID>` + a mandatory **Why:** paragraph (suppressions without rationale are lost context).
- **Parameterise** rules where the rule documents a parameter (especially relevant for scale rules — hot-table lists, threshold values).

The project rules file is *load-bearing context* — it captures the deliberate trade-offs that distinguish "this pattern is fine here" from "this pattern needs fixing." When a finding moves between findings and verified-clean across audits, the project rules file is where the reasoning lives.

## Reports

Reports land at `<project>/audits/<skill>-YYYY-MM-DD.md`. The `audits/` directory is **gitignored** in BookTracker — reports stay local to the dev machine. The reasoning: BookTracker is a public repo, and audit reports surface ongoing soft-spots we don't want to advertise unnecessarily. Living-doc summaries (`SECURITY-AUDIT.md`) stay in the repo; per-run snapshots stay local.

Each report has the same structural shape:

- **Frontmatter** — machine-readable summary counts (findings by severity, passed, suppressed)
- **Executive summary** — human scan-friendly, 2-3 paragraphs
- **Findings by severity** — critical → high → medium → low → info
- **Areas verified clean** — the durable "we checked and it was OK" record. Three pass shapes: *verified clean*, *documented concession* (the rule's pattern is present but accepted by project rules), *no applicable surface yet* (rule activates when the surface materialises)
- **Suppressed rules** — visible so they don't rot
- **Detail** — per-finding deeper context + fix guidance
- **Methodology** — what was inspected, what tools were used
- **Next-run reminder** — when re-running matters

The "Areas verified clean" section is intentional — recording what the audit covered and confirmed-clean is just as durable as recording findings. A surface that's clean today could regress; the explicit pass-with-evidence makes drift surface in the next run.

## Pilot history

The audit-skill chassis was piloted via `security-audit` (2026-05-03 onwards). Two-stack validation across BookTracker + a separate Next.js + Supabase project confirmed the chassis ports cleanly. Three durable lessons from the pilot, all rolled into subsequent skills:

1. **Project rule files belong outside `.claude/`** — Next.js's `create-next-app` defaults gitignore `.claude/`, which silently breaks rule sharing across machines. The convention is `<project>/audit-rules/<name>.md` at the repo root.
2. **"No applicable surface yet" is a valid pass shape** — recording that a rule's surface doesn't exist in the project (rather than silently omitting the rule) primes the next audit to catch the surface when it lands.
3. **Documented concession is load-bearing for scale rules in particular** — security rules are mostly boolean ("present or not"). Scale rules flag patterns that often exist deliberately. The "documented concession" pass shape is where the *reasoning* lives.

Full retro: `.claude-memory/retros/retro_security_audit_skill_poc.md`.

## When to re-run

Each skill's report ends with a "Next-run reminder" tailored to its findings. General guidance:

- **After material changes to the audited surface** — e.g. re-run `security-audit` after merging a PR that touches infra / auth / new endpoints; re-run `scale-audit` after merging a PR that adds new EF queries / list pages / background services.
- **After a related TODO lands** that would retire a documented concession (e.g. TODO #21 deploy-time migrations shipped 2026-05-18, retiring the `db_ddladmin` concession in `security-audit`).
- **On a 30-day cadence** as a baseline catch for drift that wouldn't otherwise trigger a re-run.
