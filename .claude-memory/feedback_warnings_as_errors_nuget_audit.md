---
name: feedback-warnings-as-errors-nuget-audit
description: Gotchas from enabling TreatWarningsAsErrors + clearing a NuGet security advisory (NU1903) across the .NET solution — advisory "no fix" can be stale, scope native pins, suppress per-advisory not per-severity.
metadata:
  node_type: memory
  type: feedback
---

Durable build/security lessons from the TD-11 warnings-as-errors arc (2026-06-27, closed via [[project-backend-refactor-arc]]-adjacent work; resolution in `docs/TECH-DEBT.md` TD-11). All verified on this .NET 10 / `BookTracker.slnx` repo.

**1. A NuGet advisory's "no patched version" can be stale — check the actual version list.** GitHub advisory GHSA-2m69-gcr7-jv3q (CVE-2025-6965, SQLite < 3.50.2) reported `SQLitePCLRaw.lib.e_sqlite3` affected `≤ 2.1.11` with "no patched version." But the **native-lib package had renumbered its versioning to track the bundled SQLite version**, jumping `2.1.11` → `3.50.3` — so the patched build existed, just under a version the advisory's range didn't cover. Always hit `https://api.nuget.org/v3-flatcontainer/<pkg-lowercase>/index.json` to see real published versions before concluding a vuln is unfixable.

**2. TreatWarningsAsErrors promotes NuGet-audit warnings (NU1xxx) to errors too**, not just compiler warnings — confirmed empirically (an `NU1903` became a build error under TWAE). So flipping TWAE on a repo with a vulnerable transitive package breaks the build until the vuln is pinned away or suppressed.

**3. Scope a direct native-package pin with `PrivateAssets="all"`.** Overriding a transitive native (`<PackageReference SQLitePCLRaw.lib.e_sqlite3 3.50.3>`) in a shared library flows to **every** consumer — including a different-platform build. Here the 3.50.x base package also carries an Android `.so`, which collided with the `.android` AAR in the MAUI build (`XA4301` duplicate-native). `PrivateAssets="all"` keeps the fix in the desktop/CI projects without leaking. Consequence: a leaf consumer that genuinely needs the override (a test project) must **re-pin it directly**, since `PrivateAssets="all"` stops the flow.

**4. For an unfixable-upstream vuln under TWAE, suppress the *specific advisory*, not the severity class.** `<WarningsNotAsErrors>NU1903</WarningsNotAsErrors>` exempts **every** High-severity advisory on **every** package (NU1903 is the severity-class code) — too broad. Use `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-..." />` (NuGet 6.12+/.NET 9+) to exempt exactly that CVE, so a future High advisory on any other package still fails the build. (Build-warning codes like `XA0141` have no per-package scope, so those stay a blanket `WarningsNotAsErrors` — accept the breadth and document it.)

**5. Attribute a *new* warning to your change with a stash-build.** When the MAUI build showed an `XA4301` that wasn't obviously mine, `git stash push -- <the.csproj>` then rebuild proved the pin introduced it (clean main had no XA4301) — cheaper than reasoning about NuGet asset graphs. Links: [[feedback_verify_warnings_clean_build]] (clean `--no-incremental` builds), [[feedback_review_agents_need_diff_file]].
