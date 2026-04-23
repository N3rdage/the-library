---
name: Retro — initial scaffolding (project zero → first deploy)
description: 4 days, ~50 commits — from empty repo to deployed Azure App Service with auth, CI/CD, custom domain
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** No lived recall — drawn from commit messages between 2026-04-14 (initial commit) and 2026-04-17.

**Shipped** — full project from zero in 4 days: BookTracker scaffolding → home dashboard → first Add Book page → Razor Pages → Blazor Web App migration (PR #6) → Azure Bicep infra (App Service S1, Azure SQL Basic, Log Analytics, App Insights, AAD-only auth via Easy Auth v2) → GitHub Actions CI/CD with deploy + swap workflows → optional custom domain with managed cert. By PR #20 there was a working library-tracking app deployed to Azure with users gated behind an Entra enterprise app.

**Surprise (inferred)** — the Razor Pages → Blazor Web App pivot at PR #6 was a real mid-flight architecture change. The commit message notes it cleaned up jQuery validation hacks and made `Look up` a clean async button click instead of a page-reload. Doing this *early* (before there was much to migrate) is what made it cheap. Doing it later would have been a much bigger bill.

**Lesson** — the Bicep + deploy.ps1 commit (`#8`) had four follow-up fix commits in the same PR for things only deployment surfaces: SecureString serialisation, /me vs $azCtx.Account.Id for the SQL admin lookup, opening a temp firewall rule for the deploy host, and verifying the sign-in identity. Real infra-as-code work always has these layers of "the docs lie" surfacing as you actually run it. Budget for it.

**Quotable** — the project went from `Initial commit` to `feat: GitHub Actions CI/CD with deploy + swap workflows (#9)` in three days. Setting the deployment pipeline up *while* the app was still skeleton meant every subsequent feature merged via the same pipeline.
