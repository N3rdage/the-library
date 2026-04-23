# BookTracker

A personal library-tracking web app — catalog the books you own, scan ISBNs to add them, track reading status and ratings, spot duplicates, and ask an AI assistant for recommendations. Deployed at [books.silly.ninja](https://books.silly.ninja). Code is ASP.NET Core Blazor Server (.NET 10) backed by EF Core + Azure SQL, hosted on Azure App Service behind Easy Auth.

## What this repo actually is

This project is an experiment in AI-first development. **Nearly all code in this repository is written by Claude Code**, an agentic AI coding assistant, across interactive sessions with the author. The author's role is closer to product owner, architect, and reviewer than implementer: describing features, answering design questions, reviewing plans + PRs, and deciding what ships.

A secondary goal is to learn how different interaction styles affect Claude's output over the long arc of a non-trivial project. That's why this repo tracks some things most projects wouldn't:

- [`.claude-memory/`](.claude-memory/) — persistent context for Claude across sessions: workflow preferences (`feedback_*.md`), project facts (`project_*.md`), per-feature retros (`retros/`), and working patterns (`patterns.md`). Read these for the "how the sausage is made" view — they're more interesting than the code for what this experiment actually is.
- [`SECURITY-AUDIT.md`](SECURITY-AUDIT.md) — living security posture doc. Monthly auto-generated review issue. Mostly a side-effect of the workflow, but a good artefact.
- [`GOING-PUBLIC.md`](GOING-PUBLIC.md) — planning doc for the recent flip-to-public decision. Example of the kind of deliberate review the workflow encourages.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — living design doc, updated at each structural change.
- [`TODO.md`](TODO.md) — priority-ordered table of open work + shipped items with estimate-vs-actual PR counts.

If you're curious about the AI-collaboration angle specifically, the retros are the richest source — each one captures what was surprising, what was learned, and one quotable generalisation from the arc.

## Features (what the app does)

- Catalog books with multiple **editions** (ISBN, format, publisher, date printed) and multiple **copies** per edition (condition, date acquired, notes).
- **Works** as a separate abstraction above books — handles compendiums cleanly (Shakespeare's Complete Works is one book containing 37+ works).
- **Authors** with pen-name aliasing (Richard Bachman → Stephen King rolls up).
- **Series** and **Collections** with ordering, expected counts, and gap detection.
- **ISBN lookup** via Open Library → Google Books → NLA Trove (fallback chain).
- **Barcode scanning** (html5-qrcode) + **photo OCR** (AI vision) for pre-ISBN books.
- **Duplicate detection** + merge flows across Books / Editions / Works / Authors.
- **AI assistant** — multi-provider (Anthropic direct, Azure OpenAI, Microsoft Foundry) with runtime provider toggle. Handles recommendations, collection analysis, shopping suggestions, and a "book advisor" conversational mode.
- **Shopping mode** — quickly check whether you already own a book while at a bookshop. Mobile-optimised; scanner-first on mobile.
- **PWA** — installable on mobile + desktop, launches standalone.
- Fully **mobile-responsive**; designed for the "phone in hand in the bookshop" workflow as a first-class use case.

## Stack + notable choices

- **ASP.NET Core Blazor Web App** (.NET 10, Interactive Server render mode).
- **MudBlazor** for UI components (convert-as-we-touch rollout from an earlier Bootstrap baseline).
- **EF Core + Azure SQL** for persistence; **`IDbContextFactory`** rather than scoped DbContext to handle Blazor Server's long-lived circuits.
- **Bicep + PowerShell** for infrastructure (`infra/`), with scheduled secret rotation via GitHub Actions + OIDC federation.
- **GitHub Actions** for CI, deploy-to-staging-slot, manual swap, secret rotation, and monthly security review.

## Contributing

**I'm not currently accepting external code contributions.** Part of the experiment is that every change to this codebase goes through a Claude Code session — so a PR with code from someone else doesn't fit the frame. (Running it through Claude "to review" would still mean the code wasn't written via the experiment's normal workflow.)

What's welcome:

- **Issues** with suggestions, bug reports, or curious questions about why something's done a particular way.
- **Feedback on the approach** — interaction style observations, notes on what's working or not working about AI-collaborated development, pointers to related experiments.

I may later open up to proper PRs with Claude-driven code review (tracked in TODO as a far-future item). That'd be its own experiment.

## Run locally

Requirements: Windows with PowerShell 7+, .NET 10 SDK, Docker Desktop (for the SQL container).

```powershell
docker compose up -d   # SQL Server 2022 Developer on localhost:1433

cd "path\to\the-library"
# Copy the committed templates to real config files and fill in secrets:
Copy-Item BookTracker.Web\appsettings.Example.json BookTracker.Web\appsettings.json
Copy-Item BookTracker.Web\appsettings.Development.Example.json BookTracker.Web\appsettings.Development.json

dotnet ef database update --project .\BookTracker.Data --startup-project .\BookTracker.Web
dotnet run --project .\BookTracker.Web
```

See [`CLAUDE.md`](CLAUDE.md) for the full "how I work with this repo" guide (also used by Claude Code sessions to understand conventions) and [`infra/README.md`](infra/README.md) for the Azure deployment setup.

## License

MIT — see [LICENSE](LICENSE).
