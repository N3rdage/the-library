---
name: Infra — refresh-local-db.ps1 (prod BACPAC → local Docker SQL)
description: Interim infra PR to unblock dedup UI testing with realistic data. SqlPackage BACPAC export/import, with an auth detour en route.
type: project
---

## Shipped

PR #87. `infra/refresh-local-db.ps1` — pulls a BACPAC of prod Azure SQL into the local Docker container, using SqlPackage. Same firewall dance as `deploy.ps1` (temp enable + temp rule + restore in `finally`). BACPACs land in the already-gitignored `artifacts/`. Destructive-op prompt before the local clobber; `-Force` to skip on reruns. `-SkipExport` / `-SkipImport` flags for iteration. Two commits: the initial script, then a `fix(infra):` commit swapping the auth flow after first-run feedback.

## Surprise

- **SqlPackage's `/ua:true` doesn't reliably resolve custom Entra domains.** First run hit "silly.ninja isn't in our system. Make sure you typed it correctly" in the browser auth popup. The issue wasn't permissions — Drew is the SQL AAD admin — it was that SqlPackage's browser flow hits the generic `login.microsoftonline.com` authority *without a tenant hint*, and the tenant lookup by email-domain doesn't always resolve for custom domains like `silly.ninja`. The `Connect-AzAccount -Tenant $TenantId` call earlier in the script already had tenant context; SqlPackage's separate browser flow didn't inherit it.
- **The fix is a four-line swap, not a config flag.** Pre-acquire a `database.windows.net`-scoped token from the Az session and pass it via `/AccessToken`. Bypasses the browser flow entirely. `deploy.ps1` already uses this pattern for `Invoke-Sqlcmd` after the deployment — consistency with the existing codebase turned out to be the right answer. Lesson: when a Microsoft tool offers "browser auth" and "token auth", and you're already in an authenticated PowerShell session, token auth is strictly better.
- **BACPAC exports on Basic-tier Azure SQL feel stuck but aren't.** The "Connecting to database..." message prints *before* the server-side serialisation pass begins. For a small DB that pass still takes ~2–5 minutes on Basic tier. First-run was reported as "stuck" at exactly this point — had to nudge with "it's slow, not hung; wait 5 minutes" before the real auth issue surfaced. Good prompt in user-facing tooling would say something like "Waiting for server-side serialisation (2–5 min on Basic tier)...".

## Lesson

- **Realistic test data is an infra-adjacent feature, not a stretch goal.** Drew asked for this mid-stream through the dedup series ("testing is proving difficult without representative data") — and it was absolutely right. The Preston matcher-loosening iteration on the first dedup PR happened because he saw real data; synthetic seeds wouldn't have surfaced it. The time spent on a 254-line PowerShell script paid back within the author-merge PR's smoke testing. Worth remembering: if a feature has any kind of heuristic or user-facing matching behaviour, getting realistic data in front of the developer is part of shipping it.
- **`finally` matters more than the happy path.** The firewall-open-and-restore pattern lives in `try/finally` for exactly the reason it fired on first run: user Ctrl+C'd, script aborted inside the SqlPackage call, firewall rule would have stayed open indefinitely without the `finally`. I mentioned in the response to the abort that "the aborted run may have left the firewall rule in place" — it didn't, because the `finally` ran. The safety-net pattern lived up to its name.
- **Auth debugging starts with "which authority and which tenant?"** Generic auth errors ("isn't in our system") usually mean the auth flow is asking the wrong authority. For Microsoft tools, the telltale is whether the call includes `-Tenant` / `/TenantId` / an access token. Access tokens from an already-authenticated session are the most bulletproof — they carry the tenant binding implicitly.
- **BACPAC beats custom EF copiers for infra-shape PRs.** Was tempted to write a C# script that reads all entities via EF and re-inserts into local. Would have been more code, more failure modes (FK ordering, `IDENTITY_INSERT`, schema drift), and no gain. Microsoft-sanctioned tooling for infra work, custom code for product work is a good default.

## Quotable

The BACPAC script exists because "testing is proving difficult without representative data." One user sentence in the middle of a feature series that turned into a 254-line infra PR, which then immediately unblocked the feature series it was written for. Infrastructure isn't always exciting, but the right piece of infrastructure at the right moment is worth more than any individual feature.
