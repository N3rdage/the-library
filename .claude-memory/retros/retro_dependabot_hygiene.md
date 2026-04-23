---
name: Retro — Dependabot + dependency hygiene
description: PR #19 set up Dependabot for NuGet + JS; PRs #21, #23, #25-#29 are the resulting churn. Started a discipline of accepting boring updates fast.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit messages of PRs #19, #21, #23, #25, #26, #27, #28, #29 (2026-04-15 to 2026-04-16).

**Shipped** — Dependabot configured for weekly checks on both NuGet packages and vendored JS (PR #19). Added a minimal stub `package.json` (private, not for builds) just so Dependabot could track `html5-qrcode` for static-asset version updates and security advisories. Within 24-48 hours the bot opened a series of small PRs:
- #21 — Microsoft.EntityFrameworkCore.Design 10.0.5 → 10.0.6
- #23 — Microsoft.EntityFrameworkCore.Tools 10.0.5 → 10.0.6
- #25 — fix to remove custom labels from Dependabot config (they didn't exist)
- #26 — coverlet.collector 6.0.4 → 8.0.1
- #27 — Microsoft.NET.Test.Sdk 17.14.1 → 18.4.0
- #28 — xunit.runner.visualstudio 3.1.4 → 3.1.5
- #29 — chore: align EF Core packages to 10.0.6 and group in Dependabot

**Surprise (inferred)** — PR #29 is the interesting one — it added a *grouping* rule to Dependabot's config so all the EF Core packages would update together in a single PR instead of three separate ones. That's the moment we shifted from "accept the bot's PRs as they come" to "tune the bot to fit the codebase". Worth doing once you've seen the pattern of which packages move together.

**Lesson** — accepting Dependabot updates fast is its own discipline. PRs #21 / #23 / #29 show it took 2 days from "set up Dependabot" to "noticed two packages should always update together, configured grouping". That feedback loop — bot opens PRs, you accept them, you notice patterns, you tune the bot — is the whole point. If you batch dependency updates monthly you lose this loop and end up with a giant scary update pile.

**Quotable** — by the time the project was 6 weeks old, dependency updates had become entirely background noise. That's the win: not having opinions on minor version bumps because the bot handles them and the test suite catches anything weird. Worth setting up on day 2 of any new project.
