# Security audit — living doc

Living security posture review. Per-area verdicts: **Pass** / **Concern** / **Action required**. Fixes applied in the same PR are listed at the bottom; deferred items link to `TODO.md`. Monthly scan + review is automated via `.github/workflows/security-scan.yml`.

**Last reviewed:** 2026-05-05 (monthly cycle; per-run report in `audits/security-2026-05-05.md`).

---

## 1. Git history secret scan

**Verdict:** **Pass** — no secrets committed.

- **Tool:** `gitleaks v8.30.1` (installed via `winget install gitleaks.gitleaks`). Configured to run on every PR and monthly via GitHub Actions.
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
- **`.gitignore` compliance:** `appsettings.json` and `appsettings.*.json` are excluded; only `appsettings*.Example.json` files are committed. Inspected the Example files — all sensitive fields are empty strings or obvious placeholders (`<your-docker-sa-password>`). No real values.

---

## 2. Easy Auth `excludedPaths`

**Verdict:** **Pass.**

`infra/modules/app-config.bicep:86-93` lists six paths exempted from AAD sign-in:
```
/manifest.webmanifest, /service-worker.js,
/icons/icon.svg, /icons/icon-192.png, /icons/icon-512.png, /icons/apple-touch-icon.png
```

- Easy Auth v2 `excludedPaths` uses **exact** matching, not prefix (noted in the Bicep comment). So an accidental future Blazor route at `/icons/something.png` would still require its full path to be added to the list before being publicly accessible.
- Cross-checked against all Blazor `@page` routes — none collide with any excluded path.
- All six excluded paths serve non-sensitive static content (app name, theme colour, public icons, client-side caching logic).

---

## 3. SignalR hub authentication

**Verdict:** **Pass.**

`/_blazor/*` is the SignalR hub path for Interactive Server rendering. It is **not** in `excludedPaths`, so Easy Auth requires an authenticated session before the WebSocket upgrade completes. Unauthenticated users cannot reach the hub.

---

## 4. Content-Security-Policy (CSP) headers

**Verdict:** **Action required (fix applied)** — baseline CSP added in this PR.

- **Before:** no CSP, no `X-Frame-Options`, no `X-Content-Type-Options`, no `Referrer-Policy`.
- **After this PR:**
  - `Content-Security-Policy` via `<meta>` tag in `App.razor` with these directives:
    - `default-src 'self'` — block anything not explicitly allowed.
    - `script-src 'self' 'unsafe-inline' 'unsafe-eval'` — Blazor Server framework needs inline + eval for its runtime. A nonce-based approach is a future tightening.
    - `style-src 'self' 'unsafe-inline' https://fonts.googleapis.com` — Bootstrap + MudBlazor + Google Fonts.
    - `font-src 'self' https://fonts.gstatic.com` — Roboto.
    - `img-src 'self' data: blob: https:` — cover art comes from many external HTTPS sources (Open Library, Google Books, etc.); locking to specific hosts would break the lookup pipeline as providers change.
    - `connect-src 'self' wss: ws:` — SignalR WebSocket.
    - `media-src 'self' blob:` — camera capture.
    - `base-uri 'self'`, `form-action 'self'` — constrain form posts.
  - `X-Frame-Options: DENY` (middleware in `Program.cs`) — clickjacking protection.
  - `X-Content-Type-Options: nosniff` — MIME sniffing protection.
  - `Referrer-Policy: strict-origin-when-cross-origin` — limit referrer leakage.
- **Follow-up:** the `unsafe-inline` + `unsafe-eval` directives leave a real XSS surface. Moving to a nonce-based CSP with a script-nonce middleware is tracked as a new TODO row.

---

## 5. Key Vault access paths

**Verdict:** **Pass.**

RBAC-mode vault (`enableRbacAuthorization: true`). Data-plane role assignments are narrow:
- App Service prod MI → `Key Vault Secrets User` (read-only on secrets).
- Staging slot MI → `Key Vault Secrets User`.
- `booktracker-ci` OIDC SP → `Key Vault Secrets Officer` (write, scoped to the vault — needed by the rotation workflow, PR #111).

No secrets exposed in App Service environment-variable UI as raw values; every secret setting resolves via `@Microsoft.KeyVault(SecretUri=...)` reference.

No `Key Vault Administrator` or `Contributor` at the vault scope for any managed identity — those would allow data-plane operations and policy changes.

---

## 6. Dependency vulnerabilities

**Verdict:** **Pass.**

`dotnet list BookTracker.slnx package --vulnerable --include-transitive` — zero vulnerable packages across `BookTracker.Data`, `BookTracker.Tests`, `BookTracker.Web`. Dependabot remains the forward-facing watchdog for CVEs landing after this date.

---

## 7. PII in logs

**Verdict:** **Pass, with one minor note.**

Grep of `logger.Log*` call sites in `BookTracker.Web`:

| Site | Context | Severity |
|---|---|---|
| `EditionFormatBackfillService` (4 log calls) | Operational progress + ISBN on per-row failure | Low. ISBN isn't PII; log message identifies the book under maintenance. |
| `BookLookupService:70` | `"Open Library title/author search failed for title={Title} author={Author}"` — the strings the user typed | Low. For a single-user app the "user" is the owner of the library; search queries are effectively operational telemetry. Flagged for awareness but not acted on. |
| `BookLookupService:112,156,211` | ISBN on lookup failure from Open Library / Google Books / Trove | Low. Same reasoning as above. |

No user identifiers, email addresses, tokens, or auth material in any log string. Structured logging uses named parameters correctly (no string-concat SQL-style risks).

---

## 8. SQL injection surface

**Verdict:** **Pass.**

- `FromSqlRaw` / `ExecuteSqlRaw`: zero occurrences across the solution (grep verified).
- All app-side queries use EF Core LINQ, which parameterises by construction.
- `migrationBuilder.Sql(...)` calls in `BookTracker.Data/Migrations/` interpolate values from `GenreSeed.All` and the hard-coded sub-genre arrays, never user input. The seed migration uses an `Escape()` helper that doubles single quotes defensively.

---

## 9. JS interop XSS

**Verdict:** **Pass.**

All `IJSRuntime.Invoke*` call sites use **hardcoded string identifiers** for the JS function names (`"NavbarCollapse.close"`, `"ScrollTo.element"`, `"BarcodeScanner.start/stop"`, `"PhotoCapture.start/stop/capture"`, `"chipPicker.suppressEnterAndComma"`). The function names are not user-controlled.

Arguments passed to JS:
- `ScrollTo.element` — `$"author-row-{id}"` where `id` is a typed `int`. Not injectable.
- `BarcodeScanner.start` — a DOM element id (`"barcode-reader"` or `"shopping-barcode-reader"`, hardcoded) plus a `DotNetObjectReference`. Safe.
- `PhotoCapture.start` / `capture` — hardcoded DOM id `"photo-video"`. Safe.
- `chipPicker.suppressEnterAndComma` (added 2026-05-04, multi-author chip-picker arc) — typed `ElementReference` (the picker's container `div`) + `DotNetObjectReference<MudAuthorPicker>`. The JS handler in `wwwroot/js/chip-picker-keys.js` reads `input.value` at keydown time and round-trips it to `[JSInvokable] OnCommitKey`; the .NET side trims and dedupes before adding to `Authors`, which renders via `<MudChip Text="@captured">` (Razor auto-escape). Safe.

No `innerHTML`-style JS methods are exposed to the Blazor side. No user strings flow into `eval`-equivalent patterns. The new `chip-picker-keys.js` uses only safe DOM APIs (`addEventListener`, `getAttribute`, `querySelector`, reading `input.value`).

---

## 10. Azure resource RBAC scoping

**Verdict:** **Pass, with one accepted trade-off.**

Managed-identity role assignments (Bicep + deploy-time SQL grants):

| Identity | Role | Scope | Notes |
|---|---|---|---|
| App Service prod MI | Key Vault Secrets User | KV | Read-only data plane |
| App Service prod MI | Cognitive Services User | OpenAI account | Inference only, no model management |
| App Service prod MI | SQL `db_datareader/writer/ddladmin` | Azure SQL DB | `db_ddladmin` needed for migrate-on-startup; could drop once deploy-time migrations land (TODO #20) |
| Staging slot MI | Same three roles | (mirrored) | |
| `booktracker-ci` OIDC SP | Contributor | Resource group | For artifact deploys + Bicep ops |
| `booktracker-ci` OIDC SP | Key Vault Secrets Officer | KV | For secret-rotation workflow |
| `booktracker-ci` OIDC SP | Owner of `Library-Patrons` app reg | AAD | For secret-rotation workflow |
| `booktracker-ci` OIDC SP | Graph `Application.ReadWrite.OwnedBy` | Graph | Narrow — apps it owns only |

Trade-off accepted: `db_ddladmin` on the App Service MI is broader than strict data plane but required for the migrate-on-startup pattern. Tightening to `db_datareader`/`db_datawriter` is blocked behind the "deploy-time migrations" TODO (#20).

No role assignments at subscription or management-group scope.

---

## 11. HSTS + HTTPS redirect

**Verdict:** **Pass.**

- `Program.cs:118` — `app.UseHsts()` in non-Development environments.
- `Program.cs:121` — `app.UseHttpsRedirection()` unconditionally.
- Custom domain bound with App Service managed certificate (auto-renewing). HTTPS-only binding confirmed via Bicep / deploy config.
- HSTS header sent by ASP.NET Core default middleware; the portal/CLI can verify response headers in prod if needed.

---

## Fixes applied in the PR that landed this audit

1. **`Content-Security-Policy` meta tag** added to `BookTracker.Web/Components/App.razor` (see §4 for the directive set).
2. **Security headers middleware** in `BookTracker.Web/Program.cs` setting `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`.
3. **`.github/workflows/security-scan.yml`** — gitleaks runs on every PR + weekly, opens a GitHub issue on the 1st of each month with the audit-review checklist.
4. **`SECURITY-AUDIT.md`** (this file) — durable record of the audit state.

## Deferred items (tracked in TODO.md)

- **Nonce-based CSP middleware** — replace `'unsafe-inline'` / `'unsafe-eval'` with a per-request nonce threaded through Blazor's framework inlines. Real XSS tightening; non-trivial implementation work.
- **Scrub user-typed search queries from `BookLookupService` log lines** — optional hardening; low priority given single-user deployment.
- **Drop `db_ddladmin`** from App Service MI SQL grants — unblocked once `dotnet ef migrations bundle` replaces migrate-on-startup (already-tracked TODO #20).

## Monthly review process

`.github/workflows/security-scan.yml` opens a GitHub issue on the 1st of every month titled "Security review — YYYY-MM". The issue body links back to this file with a checklist:

- [ ] Gitleaks scan of past month: clean?
- [ ] Any new routes under `excludedPaths` scope? Recheck §2.
- [ ] Any new `IJSRuntime.Invoke*` sites with user input? Recheck §9.
- [ ] Any new `FromSqlRaw` / `ExecuteSqlRaw`? Recheck §8.
- [ ] `dotnet list package --vulnerable` clean?
- [ ] Any new managed-identity role assignments in Bicep? Recheck §10.
- [ ] `Last reviewed` date updated at the top of this file.

Close the issue once the review is done; push any updates to this file in the same PR.
