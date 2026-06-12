---
name: SQL DB restart breaks SqlClient pool zombies
description: Slot-swap can leave prod wedged with both slots Running but not serving. Recovery ladder: slot Stop+Start → SQL DB restart → App Service Plan SKU change (fresh compute). If SCM/Log Stream also time out, the wedge is instance-level — go straight to the plan migration.
type: project
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When both App Service slots report `Running` but neither serves traffic, and Stop+Start of both slots doesn't recover them, restart the SQL database (Portal preview feature, or `az sql db restart`). This force-kills every server-side connection.

**Why:** SqlClient's connection pool can hold "zombie" connections — physical connections the client believes are alive but the server has lost or is no longer responding to. Worker threads waiting on those connections never time out. App Service's graceful-shutdown sequence waits for those threads, so `Stop` reports success while the worker is still partly hung. The fresh worker that `Start` spawns immediately tries to use the pool and inherits the same wedge because the SQL-side state outlives the App Service worker process.

**How to apply:** Use this when:
- Both prod and staging slots show state `Running` via `az webapp show`
- Neither slot serves HTTP requests (curl times out, log streams silent, App Insights Live Metrics silent)
- `az webapp stop` + `az webapp start` on both slots has been tried and didn't recover service
- SQL DB itself shows `Online` via `az sql db show -n <db> -g <rg> -s <server> --query status -o tsv`

Restart the SQL database. Workers will get fast `connection died` errors instead of indefinite hangs, hung threads will exit, and the next worker spawn will start with a clean pool. Distinct recovery move from the April incident — that one was AAD-token cache lag, where Stop+Start of slots was sufficient.

First confirmed: 2026-05-08 perf-fix deploy. The pre-deploy bug (a render-tree cartesian on the Publishers page) wedged workers in a way that obstructed the deploy of its own fix; SQL-restart broke the wedge cleanly.

**Escalation ladder (recovery order, gentlest first):**
1. Slot Stop+Start (sufficient for the April AAD-token-cache-lag incident).
2. SQL DB restart (`az sql db restart`, or Portal SQL DB → Restart preview; force-kills server-side connections — the 2026-05-08 fix).
3. **App Service Plan compute migration** — change the SKU to force every worker onto a fresh VM. This is the escalation when SQL restart is NOT enough.

Second confirmed: 2026-06-11, again triggered by a slot swap. Signature was worse than 2026-05-08 — both slots `Running`, DB `Online`, but **504 gateway timeout, dead-silent Log Stream, zero App Insights traffic, and SCM itself eventually 504'd** (the worker instance was wedged below the app layer, not just the connection pool). SQL DB restart did NOT recover it this time, nor did slot stop+start. Resolution required step 3: a plan SKU change (legacy **S1 → Premium V3**, conveniently cheaper + higher spec) to migrate to fresh compute. Tell: when SCM (`*.scm.azurewebsites.net`) times out / 504s and Log Stream returns "The request timed out", the wedge is at the instance level — go straight to the plan migration; don't waste time on app/SQL restarts. Azure status was green throughout (not a regional incident). `az sql db restart` was unavailable on the installed CLI version — use the Portal Restart preview button.
