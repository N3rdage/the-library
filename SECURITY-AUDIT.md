# Security audit — living doc

Living security posture review. Per-area verdicts: **Pass** / **Concern** / **Action required**. Fixes applied in the same PR are listed at the bottom; deferred items link to `TODO.md` or to a private backlog id (`LIB-nnn`). Monthly scan + review is automated via `.github/workflows/security-scan.yml`.

**Last reviewed:** 2026-11-14, two ways: Drew's `security-audit` skill run on his laptop (per-run report local, `audits/security-2026-11-14.md`), plus a by-hand re-application of this document and `audit-rules/security.md` in the autonomous agent's container, which cannot load the skill. Totals from Drew's run: 0 critical / 1 high / 2 medium / 6 low / 3 info. The High, both Mediums and two Lows were fixed in [#465](https://github.com/N3rdage/the-library/pull/465) and [#471](https://github.com/N3rdage/the-library/pull/471) before this refresh. The hand sweep added further items; of its three Mediums, LIB-016 was fixed in [#481](https://github.com/N3rdage/the-library/pull/481) and LIB-026 in [#485](https://github.com/N3rdage/the-library/pull/485). Every open item is tracked in the private backlog as `LIB-nnn`; this public doc names the area, the verdict and the id only. Prior review: 2026-07-24 (before that, 2026-05-05).

**Tooling caveat.** The skill's default and template rules (22, inferred from Drew's 25 minus BOOK-001..003; the repo does not state the number) live user-global on Drew's machine, not in this repo (see `docs/audit-skills.md`). What a headless agent can apply is `audit-rules/security.md` plus this document, so a hand sweep cannot reproduce the skill's "N of 25 rules clean" count. Drew's run: 19 of 25 rules clean.

---

## 1. Git history secret scan

**Verdict:** **Pass** — no secrets committed.

- **Tool:** `gitleaks v8.30.1` (installed via `winget install gitleaks.gitleaks`). Runs on every PR and monthly via `.github/workflows/security-scan.yml`. A full-history re-run is Drew's to do on the laptop.
- **Result:** 228 commits scanned (~13.7 MB), zero leaks.
- **Belt-and-braces pattern search (manual grep across full history):**
  - `sk-ant-*` (Anthropic-key shape) — 2 hits, both documentation placeholders (`sk-ant-…` with an ellipsis in `infra/README.md`). Not real keys.
  - `sk-proj-*` / `sk-*` (OpenAI) — 0 hits.
  - `AIza[0-9A-Za-z_-]{35}` (Google API key) — 0 hits.
  - `BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY` — 0 hits.
  - `gh[pous]_[A-Za-z0-9_]{36,}` (GitHub PAT) — 0 hits.
  - `eyJ[...]*.eyJ[...]*` (JWT) — 0 hits.
  - 86-char base64 + `==` (Azure storage key shape) — 1 hit in `wwwroot/lib/html5-qrcode/html5-qrcode.min.js`. Third-party minified library; false positive.
  - `BookTracker!Dev1` (docker-compose SA default) — 3 hits, all in documented dev-only defaults (`docker-compose.yml`, `CLAUDE.md`, `infra/refresh-local-db.ps1`). Not a production secret.
  - `Server=...Password=` — 1 hit, same `BookTracker!Dev1` dev default.
- **Personal-data grep of the tracked tree (2026-11-14):** emails, phone numbers, surnames, non-public names, tenant/subscription/client GUIDs, public IPs, internal hostnames. Clean apart from a Windows profile username in `CLAUDE.md` and two retro files (Info, LIB-015) and a placeholder sign-in name in one test file (LIB-025, being replaced). See also `audit-rules/security.md` BOOK-003.
- **`.gitignore` compliance:** `appsettings.json` and `appsettings.*.json` are excluded; only `appsettings*.Example.json` files are committed. Inspected the Example files — all sensitive fields are empty strings or obvious placeholders (`<your-docker-sa-password>`). No real values. Keystore / certificate / `.env` patterns are to be added (LIB-027).

---

## 2. Easy Auth `excludedPaths`

**Verdict:** **Pass.**

`infra/modules/app-config.bicep:147-155` lists seven paths exempted from AAD sign-in, applied to both slots:
```
/manifest.webmanifest, /service-worker.js,
/icons/icon.svg, /icons/icon-192.png, /icons/icon-512.png, /icons/apple-touch-icon.png,
/warmup
```

- Easy Auth v2 `excludedPaths` uses **exact** matching, not prefix (noted in the Bicep comment). So an accidental future Blazor route at `/icons/something.png` would still require its full path to be added to the list before being publicly accessible. The exact-match claim is not testable from the repo; a live probe is Drew's.
- Cross-checked against all Blazor `@page` routes — none collide with any excluded path.
- The six PWA paths serve non-sensitive static content (app name, theme colour, public icons, client-side caching logic).
- `/warmup` is the slot-swap warm-up probe added with deploy-time migrations. Its anonymous behaviour: Low (LIB-013).

---

## 3. SignalR hub authentication

**Verdict:** **Pass.**

`/_blazor/*` is the SignalR hub path for Interactive Server rendering. It is **not** in `excludedPaths`, so Easy Auth requires an authenticated session before the WebSocket upgrade completes. Unauthenticated users cannot reach the hub. No other hubs exist.

---

## 4. Content-Security-Policy (CSP) headers

**Verdict:** **Pass, with a concern.**

- `Content-Security-Policy` via `<meta>` tag at `BookTracker.Web/Components/App.razor:32`; the live tag matches this directive list:
  - `default-src 'self'` — block anything not explicitly allowed.
  - `script-src 'self' 'unsafe-inline' 'unsafe-eval'` — Blazor Server framework needs inline + eval for its runtime. A nonce-based approach is a future tightening.
  - `style-src 'self' 'unsafe-inline' https://fonts.googleapis.com` — Bootstrap + MudBlazor + Google Fonts.
  - `font-src 'self' https://fonts.gstatic.com` — Roboto.
  - `img-src 'self' data: blob: https:` — cover art comes from many external HTTPS sources (Open Library, Google Books, etc.); locking to specific hosts would break the lookup pipeline as providers change.
  - `connect-src 'self'` — SignalR WebSocket and fetches to the app's own origin only. Until 2026-11-15 this was `'self' ws: wss:`, which let a script opened by an injected tag reach any WebSocket host; CSP Level 3 browsers (current Chromium, Firefox and Safari) treat a same-origin `ws:`/`wss:` connection as matching `'self'`, so the bare `'self'` keeps the Blazor circuit working (LIB-019, #501).
  - `media-src 'self' blob:` — camera capture.
  - `base-uri 'self'`, `form-action 'self'` — constrain form posts.
- Headers middleware in `BookTracker.Web/ProgramSetup.cs:215-224`:
  - `X-Frame-Options: DENY` — clickjacking protection. The meta-tag CSP cannot express `frame-ancestors`; this header covers it.
  - `X-Content-Type-Options: nosniff` — MIME sniffing protection.
  - `Referrer-Policy: strict-origin-when-cross-origin` — limit referrer leakage.
  - `Permissions-Policy: camera=(self), microphone=(), geolocation=(), payment=()` (added 2026-07-24) — deny powerful browser features by default; camera allowed same-origin only for the `/bookshop` barcode scanner (`getUserMedia`). Closed **SEC-004** from the 2026-07-24 run.
- **Concern:** `unsafe-inline` + `unsafe-eval` remain; the nonce-based CSP is still `TODO.md` row 2.

---

## 5. Key Vault access paths

**Verdict:** **Pass, with a concern.**

RBAC-mode vault (`enableRbacAuthorization: true`). Data-plane role assignments are narrow:
- App Service prod MI → `Key Vault Secrets User` (read-only on secrets).
- Staging slot MI → `Key Vault Secrets User`.
- `booktracker-ci` OIDC SP → `Key Vault Secrets Officer` (write, scoped to the vault — needed by the rotation workflow, PR #111).

No secrets exposed in App Service environment-variable UI as raw values; every secret setting resolves via `@Microsoft.KeyVault(SecretUri=...)` reference.

No `Key Vault Administrator` or `Contributor` at the vault scope for any managed identity — those would allow data-plane operations and policy changes.

The pull-request federated credential on `booktracker-ci` was removed from `infra/setup-github-oidc.ps1` in #465; removing it on the Entra side is Drew's (LIB-001).

- **Concern:** vault deletion-protection settings (`infra/modules/keyvault.bicep`). Decision pending (LIB-010).

---

## 6. Dependency vulnerabilities

**Verdict:** **Pass.**

`dotnet list package --vulnerable --include-transitive` on 2026-11-14 across all nine projects (the eight in `BookTracker.slnx` plus `BookTracker.Mobile`, which is outside the solution on purpose): zero vulnerable packages. Dependabot remains the forward-facing watchdog for CVEs landing after this date.

- The two direct pins from #462 (`System.Security.Cryptography.Pkcs` 10.0.13 in Data and Web) are still present; revisit them with the EF Core 10.0.15 bump.
- Dependabot's `nuget` entry is rooted at `/`, but `BookTracker.Mobile` sits outside `BookTracker.slnx`, so until 2026-11-15 the updater never reached it and none of the Dependabot commits in the history touched its project file. Fixed in #485 (LIB-026): the entry now lists `/BookTracker.Mobile` as a second directory, so the Android app's packages (MSAL, ZXing) get update PRs from the next weekly run.

---

## 7. PII in logs

**Verdict:** **Pass, with notes.**

Grep of `logger.Log*` call sites in `BookTracker.Web`:

| Site | Context | Severity |
|---|---|---|
| `EditionFormatBackfillService` (4 log calls) | Operational progress + ISBN on per-row failure | Low. ISBN isn't PII; log message identifies the book under maintenance. |
| `BookLookupService:100` | Open Library title/author search failure, with the strings the user typed | Low. For a single-user app the "user" is the owner of the library; search queries are effectively operational telemetry. Flagged for awareness but not acted on. |
| `BookLookupService:161,180,225,282` | ISBN on lookup failure from the providers | Low. Same reasoning as above. |
| `BlobBookCoverStorage.cs:55,65` | Cover mirror logs source URLs | Low. Operational telemetry. |
| `AIAssistantService.cs:84` | Raw model output | Low. Operational telemetry. |
| `WishlistViewModel.cs:117,192` | Typed queries | Low. Operational telemetry. |

No user identifiers, email addresses, tokens, or auth material in any log string. Structured logging uses named parameters correctly (no string-concat SQL-style risks). No reads of `X-MS-CLIENT-PRINCIPAL*` or of request headers anywhere.

`UserTelemetryInitializer.cs:16-19` would stamp `User.Identity.Name` on telemetry, but the app registers no authentication scheme, so `HttpContext.User` is expected to be empty on App Service and nothing sent. That is a runtime claim that cannot be verified from the repo; confirm it against Application Insights on the next review, and revisit if an authentication scheme is added.

---

## 8. SQL injection surface

**Verdict:** **Pass.**

- `FromSqlRaw` / `ExecuteSqlRaw`: zero occurrences across the solution (grep verified). Also no `FromSqlInterpolated` / `ExecuteSqlInterpolated` / `SqlQuery`.
- All app-side queries use EF Core LINQ, which parameterises by construction.
- `migrationBuilder.Sql(...)` calls in `BookTracker.Data/Migrations/` (15 migration files) carry seed data only: they interpolate values from `GenreSeed.All` and the hard-coded sub-genre arrays, never user input. The seed migration uses an `Escape()` helper that doubles single quotes defensively.

---

## 9. JS interop XSS

**Verdict:** **Pass.**

All 17 `IJSRuntime.Invoke*` call sites use **hardcoded string identifiers** for the JS function names (`"NavbarCollapse.close"`, `"BarcodeScanner.start/stop"`, `"PhotoCapture.start/stop/capture"`, `"chipPicker.suppressEnterAndComma"`, …). The function names are not user-controlled. `ScrollTo.element` no longer has a call site.

Arguments passed to JS:
- `BarcodeScanner.start` — a DOM element id (`"barcode-reader"` or `"shopping-barcode-reader"`, hardcoded) plus a `DotNetObjectReference`. Safe.
- `PhotoCapture.start` / `capture` — hardcoded DOM id `"photo-video"`. Safe.
- `chipPicker.suppressEnterAndComma` (added 2026-05-04, multi-author chip-picker arc) — typed `ElementReference` (the picker's container `div`) + `DotNetObjectReference<MudAuthorPicker>`. The JS handler in `wwwroot/js/chip-picker-keys.js` reads `input.value` at keydown time and round-trips it to `[JSInvokable] OnCommitKey`; the .NET side trims and dedupes before adding to `Authors`, which renders via `<MudChip Text="@captured">` (Razor auto-escape). Safe.

No `innerHTML`-style JS methods are exposed to the Blazor side. No user strings flow into `eval`-equivalent patterns. `wwwroot/js/bookshop-page.js` builds markup with `innerHTML`, but every dynamic value passes through its `esc()` helper (the only unescaped value is an integer rating). No `MarkupString` anywhere.

---

## 10. Azure resource RBAC scoping

**Verdict:** **Pass, with a concern.**

Managed-identity role assignments (Bicep + deploy-time SQL grants):

| Identity | Role | Scope | Notes |
|---|---|---|---|
| App Service prod MI | Key Vault Secrets User | KV | Read-only data plane |
| App Service prod MI | Cognitive Services User | OpenAI account | Inference only, no model management |
| App Service prod MI | SQL `db_datareader/writer` | Azure SQL DB | Data plane only — `db_ddladmin` dropped 2026-05-18 (TODO #21 PR B) once deploy-time migrations landed |
| Staging slot MI | Same three roles | (mirrored) | |
| `booktracker-ci` OIDC SP | Contributor | Resource group | For artifact deploys + Bicep ops |
| `booktracker-ci` OIDC SP | Key Vault Secrets Officer | KV | For secret-rotation workflow |
| `booktracker-ci` OIDC SP | Owner of `Library-Patrons` app reg | AAD | For secret-rotation workflow |
| `booktracker-ci` OIDC SP | Graph `Application.ReadWrite.OwnedBy` | Graph | Narrow — apps it owns only |
| `booktracker-ci` OIDC SP | SQL `db_datareader/writer/ddladmin` | Azure SQL DB | Used by the deploy-time EF migration bundle (`.github/workflows/deploy.yml` + `swap.yml`). `db_ddladmin` lives here now, scoped to CI workflow runs only. |
| `booktracker-apk-upload` OIDC SP | Storage Blob Data Contributor | `bookshelf-builds` container only | Private container. Login only via the approval-gated `bookshelf-apk` environment (#471). |

No role assignments at subscription or management-group scope. `booktracker-ci` no longer has a `pull_request` federated credential (#465).

Public containers `book-covers` / `book-covers-staging` are `publicAccess: Blob` (readable, not listable).

- **Concern:** scope of the storage credential the web app holds. Decision pending (LIB-020).

---

## 11. HSTS + HTTPS redirect

**Verdict:** **Pass** (policy raised 2026-11-15, LIB-004, #487).

- `BookTracker.Web/ProgramSetup.cs` — `ConfigureServices` registers `AddHsts(ConfigureHsts)`; `ConfigureHsts` sets `MaxAge` to 365 days, `IncludeSubDomains` true, `Preload` false. `ConfigureMiddleware` calls `app.UseHsts()` in non-Development environments and `app.UseHttpsRedirection()` unconditionally.
- Until 2026-11-15 nothing called `AddHsts`, so the framework default shipped: 30 days, same host only. The one-year value meets the six-month floor in `audit-rules/security.md` BOOK-007. `Preload` stays off: submitting `silly.ninja` to the browser preload list affects every subdomain and is Drew's call.
- Guarded by `BookTracker.Tests/Security/HstsOptionsTests.cs`, which builds the real host in the Production environment and resolves the registered `HstsOptions`, so dropping the registration fails the test.
- Custom domain bound with App Service managed certificate (auto-renewing). `httpsOnly: true` and TLS 1.2 minimum in `infra/modules/appservice.bicep` hold.
- Expected response header after the staging deploy: `strict-transport-security: max-age=31536000; includeSubDomains`.

---

## 12. Outbound fetches (cover mirror and lookups)

**Verdict:** **Pass, with open items.**

The cover mirror (`BookTracker.Web/Services/Covers/BlobBookCoverStorage.cs`, run by `CoverMirrorBackgroundService`) fetches upstream cover URLs stored on editions and books and copies them into the public `book-covers` container. Limits applied since 2026-11-15 (LIB-006, #486; see BOOK-008):

- **Content type:** only `image/jpeg`, `image/png`, `image/gif` and `image/webp` (`CoverImageProcessor.AllowedRawExtensions`; `image/jpg` is read as `image/jpeg`). Anything else, including SVG and a missing type, is skipped with a logged warning and the row keeps its upstream URL. The same allowlist applies to user uploads through `UploadAsync`.
- **Size:** at most 10 MiB (`BlobBookCoverStorage.MaxDownloadBytes`), checked against `Content-Length` before the body is read and again on the bytes actually read, since `Content-Length` can be absent or wrong.
- **Time:** the response is read with `ResponseHeadersRead`, so the body is bounded separately by a linked cancellation source set to the client's 30 s timeout; a server that sends headers and then stalls is skipped.
- **Decode failure** of an allowed type still stores the raw bytes with a warning (Drew's call; whether to keep that fallback is LIB-007).
- **Open item:** URL validation (scheme, private or link-local addresses, redirects) is not applied yet (LIB-017).

Tests: `BookTracker.Tests/Services/Covers/BlobBookCoverStorageTests.cs` and `CoverImageProcessorTests.cs`.

Lookups:
- Lookup providers receive only escaped, length-checked input; the lookup `HttpClient` has a 25 s timeout.
- Lookup provider credential handling: **Concern** (LIB-018).

---

## 13. CI and workflow posture

**Verdict:** **Pass, with open items.**

- Every `uses:` is SHA-pinned with a `# vN` comment, and Dependabot's `github-actions` ecosystem is active (#465). See BOOK-005.
- `permissions:` declared on every workflow.
- `deploy.yml` and `swap.yml` share the `sql-firewall-window` concurrency group. See BOOK-009.
- No `pull_request_target`; no untrusted event text in `run:` scripts. No cloud credential in pull-request workflows (BOOK-004).
- The APK build runs in an approval-gated environment with its own upload-only identity (#471).
- The orchestrator GitHub App has no Workflows permission, so workflow edits are the operator session's.
- `deploy.yml` is two jobs (#481): a `package` job that builds with no `id-token`, and a `deploy` job that signs in to Azure and compiles nothing. Pass (closed LIB-016).
- **Open items:** module pinning in the rotation job (LIB-008), step ordering in the APK job (LIB-021).

---

## 14. Bookshelf (Android) posture

**Verdict:** **Pass, with concerns.**

- MSAL public client, no broker; the redirect intent filter is exact on scheme/host/path.
- Manifest permissions: network state, internet, camera only.
- No cleartext config and no `http://` in code; no custom certificate validation in any configuration.
- Catalog SQLite and covers live under app-private storage and hold no identity data.
- **Concerns:**
  - Manifest application attributes (LIB-014, #489, needs Drew's yes).
  - Token-cache documentation versus implementation (LIB-023).
  - Installed builds are Debug (LIB-024, decision).

---

## Fixes applied in the PR that landed this audit

1. **`Content-Security-Policy` meta tag** added to `BookTracker.Web/Components/App.razor` (see §4 for the directive set).
2. **Security headers middleware** (now in `BookTracker.Web/ProgramSetup.cs`) setting `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`.
3. **`.github/workflows/security-scan.yml`** — gitleaks runs on every PR + weekly, opens a GitHub issue on the 1st of each month with the audit-review checklist.
4. **`SECURITY-AUDIT.md`** (this file) — durable record of the audit state.

## Changes since 2026-07-24

- **#465** — actions pinned to SHAs; local compose ports bound to loopback; shared concurrency group for deploy and swap; pull-request federated credential removed from the OIDC setup script; `permissions:` blocks on every workflow.
- **#471** — APK workflow runs in the approval-gated `bookshelf-apk` environment with its own upload-only Azure identity.
- **#477** — CODEOWNERS gives `.github/` and `infra/` to Drew.
- **#481** — deploy workflow split into a `package` job with no Azure rights and a `deploy` job that compiles nothing (LIB-016).
- **#485** — Dependabot's `nuget` entry also covers `BookTracker.Mobile` (LIB-026).

## Deferred items

- **Nonce-based CSP middleware** (`TODO.md` row 2, still open) — replace `'unsafe-inline'` / `'unsafe-eval'` with a per-request nonce threaded through Blazor's framework inlines. Real XSS tightening; non-trivial implementation work.
- **Scrub user-typed search queries from `BookLookupService` log lines** — optional hardening; low priority given single-user deployment.
- **Pending decisions (private backlog):** LIB-007, LIB-010, LIB-011, LIB-012, LIB-020, LIB-022, LIB-024.
- ~~**Drop `db_ddladmin`** from App Service MI SQL grants — unblocked once `dotnet ef migrations bundle` replaces migrate-on-startup~~ **Resolved 2026-05-18** by [PR #275](https://github.com/N3rdage/the-library/pull/275) (deploy-time migration bundle) + PR B (runtime MI dropped to `db_datareader/writer`, GH OIDC SP holds the new `db_ddladmin` scoped to CI runs only). See §10.

## Monthly review process

`.github/workflows/security-scan.yml` opens a GitHub issue on the 1st of every month titled "Security review — YYYY-MM". The issue body links back to this file with a checklist:

- [ ] Gitleaks scan of past month: clean?
- [ ] Any new routes under `excludedPaths` scope? Recheck §2.
- [ ] Any new `IJSRuntime.Invoke*` sites with user input? Recheck §9.
- [ ] Any new `FromSqlRaw` / `ExecuteSqlRaw`? Recheck §8.
- [ ] `dotnet list package --vulnerable` clean?
- [ ] `dotnet list package --vulnerable` run on `BookTracker.Mobile` too (it is outside the solution)?
- [ ] Any new managed-identity role assignments in Bicep? Recheck §10.
- [ ] Any new workflow or change to `permissions:` / `id-token`? Recheck §13.
- [ ] Any Bookshelf manifest or auth change? Recheck §14.
- [ ] `Last reviewed` date updated at the top of this file.

Close the issue once the review is done; push any updates to this file in the same PR.
