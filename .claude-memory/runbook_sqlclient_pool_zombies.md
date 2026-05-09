---
name: SQL DB restart breaks SqlClient pool zombies
description: When both App Service slots report Running but won't serve and Stop+Start doesn't help, restart the SQL database to break stuck connection-pool state.
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
