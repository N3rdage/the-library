---
name: Security audit — living doc + automation
description: First full security posture walkthrough. All 11 audit areas either passed cleanly or got a baseline fix in the same PR. Durable artefacts: SECURITY-AUDIT.md as a living doc, gitleaks on every PR + weekly, and a monthly GitHub-issue reminder that forces a human re-read. The interesting design call was the "living doc + monthly issue" cadence instead of a point-in-time audit report.
type: project
---

## Shipped

PR #112 — TODO #2 (Security audit) delivered as one PR covering investigation, fixes, and automation.

**All 11 audited areas either passed or got a fix landed alongside the finding:**
1. Git history secret scan — clean (`gitleaks v8.30.1`, 228 commits, zero hits + manual pattern sweep).
2. Easy Auth `excludedPaths` — pass (exact-match, no Blazor route collisions).
3. SignalR hub auth — pass (`/_blazor/*` gated).
4. **CSP headers** — action required → **fix applied**: baseline `<meta>` CSP in `App.razor` + `X-Frame-Options` / `X-Content-Type-Options` / `Referrer-Policy` via response-header middleware.
5. KV access paths — pass (RBAC, read-only Secrets User for runtime MIs).
6. Dependency vulnerabilities — pass (`dotnet list package --vulnerable` clean).
7. PII in logs — pass (one low-severity note on user-typed search queries; acceptable for a single-user app).
8. SQL injection surface — pass (zero `FromSqlRaw`/`ExecuteSqlRaw`).
9. JS interop XSS — pass (hardcoded function names, typed args).
10. Azure RBAC scoping — pass (one accepted trade-off on `db_ddladmin`, already tracked via TODO #20).
11. HSTS + HTTPS redirect — pass.

**Durable artefacts:**
- `SECURITY-AUDIT.md` — living doc, not a point-in-time PDF. Per-area verdicts + "Last reviewed" timestamp at the top + deferred-items section linking into `TODO.md`.
- `.github/workflows/security-scan.yml` — two jobs in one workflow. Gitleaks on every PR + weekly Monday cron. Separately, a monthly job (1st of every month at 06:00 UTC) opens a "Security review — YYYY-MM" GitHub issue with a checklist pointing at each § of the audit doc.
- One new TODO (#2, nonce-based CSP) capturing the follow-up that the baseline CSP deliberately deferred.

## Surprise

- **Gitleaks caught zero leaks. The belt-and-braces manual sweep caught zero leaks. The surprise was the quality of the existing practice.** The project's `.gitignore` hygiene (real `appsettings.*.json` excluded, committed `appsettings.*.Example.json` templates with empty strings), the `@secure()` decorator on Bicep params, the KV-reference pattern for app settings, and the documented-dev-only docker SA password together mean there's literally no path for a production secret to reach a commit. The most plausible pattern hit (`sk-ant-…`) turned out to be a documentation placeholder with an ellipsis, and the base64 shape that tripped one grep was `html5-qrcode.min.js`. The lesson isn't "gitleaks is noisy" or "grep was enough" — it's that **the absence of leaks is evidence of several small upstream hygiene decisions compounding**. Worth calling out because "we have never leaked a secret" isn't luck; it's a stack of deliberate defaults nobody ever had to explicitly think about again after setting up.
- **Living doc + monthly issue is a meaningfully different shape from a point-in-time audit report.** The traditional security audit deliverable is "PDF dated 2026-04-23, here are our findings, hope you review it again someday." A living doc with a scheduled GitHub-issue reminder makes "review again someday" concrete: the issue appears, it blocks nothing but visibly nags, and closing it requires you to actually re-diff the doc against the current codebase. Per-area checklist in the issue body means the reviewer (me, Drew, future-me) re-reads each section header and confirms it still holds. This was ~30 lines of `github-script` versus a calendar reminder or a custom tool.
- **Meta-tag CSP and header-based security are complements, not substitutes.** I initially thought "just use a `<meta http-equiv>` tag and done." Half-true: `frame-ancestors`, `report-uri`, and `sandbox` directives don't work via meta tag; browsers intentionally ignore them there. Had to split the implementation: meta tag for the core `src` directives (easy, one line in App.razor), middleware for the header-only directives (`X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`). The split matches how browsers actually parse CSP: some directives are only honored via one delivery channel.
- **Blazor Server's `unsafe-inline` / `unsafe-eval` necessity is a real compromise.** Blazor Server inlines framework state into scripts that execute during hydration; without `unsafe-inline` + `unsafe-eval` on `script-src`, the page breaks. The honest framing: a baseline CSP without nonces buys you control over *sources* (no third-party scripts, no unexpected `connect-src` destinations) but not protection from inline script injection. That's still meaningful — the common XSS vectors include "inject a `<script src=evil.com>` tag", which the baseline CSP blocks even with `unsafe-inline`. But the framing should be "we're defending against a specific subset of XSS, not XSS in general" until the nonce-based tightening lands.

## Lesson

- **Bundle investigation + fix + follow-up-tracking into one PR.** The temptation with an audit is to ship a report and let each finding become its own ticket, which then doesn't get worked. This PR's shape — investigate, apply the obvious safe fixes in the same delta, add tracked TODOs for the un-obvious ones — means that by the time the review is over, the immediate hygiene has shifted up a step *and* the residual items are visible. Compare to "here's a 20-finding PDF, assign follow-up": the finding that never becomes a PR is the one that rots. Structural principle: deliver the work-product alongside the evaluation.
- **Schedule the re-review or it won't happen.** Security posture drifts as features land. The `security-scan.yml` workflow's monthly-issue job is the forcing function — every month a new issue shows up, and leaving it open indefinitely becomes visible friction. Without that, "review quarterly" is a note that decays in 90 days. A CI workflow that files an issue is a ~30-line durable nag; a calendar reminder is 30 seconds of setup that gets snoozed.
- **Gitleaks-action is free for personal repos (without a license key). Use it by default on any repo that might accept external PRs.** Cost zero, workflow budget negligible, catches the one time someone pastes an `.env` into a PR description-exhibit file.
- **"No findings" is a valid audit outcome worth writing down.** Every section of `SECURITY-AUDIT.md` includes a verdict even when the verdict is Pass; the Pass sections document *how the pass was verified* (tool used, grep pattern, Bicep references). On next month's review, "is this still Pass?" has a concrete check to re-run. If the doc only recorded actionable findings, the Pass areas would silently regress without anyone noticing.
- **Living docs beat point-in-time reports for anything that changes.** Security posture, architecture, onboarding, runbooks — all things that drift. A doc with a `Last reviewed` header, per-section verdicts, and a scheduled re-review is a durable artefact. A PDF is a fossil.

## Quotable

"The absence of leaks is evidence of several small upstream hygiene decisions compounding." The `.gitignore` rule that excludes `appsettings.*.json` while re-including `appsettings.*.Example.json` is ~5 lines of config. The `@secure()` decorator on Bicep params is one attribute per secret. The KV-reference pattern for app settings is a convention. Individually trivial; collectively, the reason a full git-history secret scan found nothing. When you're evaluating a codebase's security posture and the scan comes up empty, the question isn't "did the scan work?" — it's "what's the set of small practices upstream of this that made the empty result the correct one?" Those practices are the artefact worth protecting.
