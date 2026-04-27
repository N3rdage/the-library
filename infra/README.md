# Azure infrastructure

Provisions the Azure footprint for BookTracker into a single resource group. Everything reachable from the App Service is locked behind Private Endpoints; the only public surface is the App Service itself (gated by Easy Auth).

## What gets created

All tagged with `Client = Drew` and `Environment = Production`.

| Resource | Name pattern | Region | Notes |
|---|---|---|---|
| Resource group | `rg-booktracker-prod` | `australiaeast` | holds everything; tags inherited |
| Primary VNet | `booktracker-vnet` (`10.0.0.0/16`) | `australiaeast` | `app-integration` (`10.0.1.0/24`, delegated to App Service) + `private-endpoints` (`10.0.2.0/24`) |
| Secondary VNet | `booktracker-vnet-eastus2` (`10.1.0.0/16`) | `eastus2` | `private-endpoints` (`10.1.2.0/24`); hosts the OpenAI PE |
| VNet peering | `to-…` (both directions) | — | bidirectional pair so the App Service can reach the OpenAI PE |
| App Service plan | `booktracker-plan` | `australiaeast` | Linux, S1 |
| App Service | `booktracker-<hash>` | `australiaeast` | Linux, DOTNETCORE\|10.0; VNet integration with `vnetRouteAllEnabled` |
| Staging slot | `staging` on the App Service | `australiaeast` | own system-assigned identity; CI deploys here, `swap.yml` promotes |
| System-assigned managed identities | on prod + staging slots | — | granted SQL DB roles + Key Vault Secrets User + Cognitive Services User on OpenAI |
| Azure SQL logical server | `booktracker-sql-<hash>` | `australiaeast` | **AAD-only auth**; `publicNetworkAccess = Disabled` by default |
| Azure SQL database (prod) | `booktracker` | `australiaeast` | Basic tier, 5 DTU; reached by the prod slot only |
| Azure SQL database (staging) | `booktracker-staging` | `australiaeast` | Basic tier, 5 DTU; reached by the staging slot only. Empty on first deploy — migrate-on-startup creates the schema. |
| Azure SQL Private Endpoint | `booktracker-sql-<hash>-pe` | `australiaeast` | in primary `private-endpoints` subnet; serves both DBs |
| Key Vault | `booktracker-kv-<hash>` | `australiaeast` | Standard, RBAC auth, `defaultAction = Deny`; holds AuthClientSecret + AI keys |
| Key Vault Private Endpoint | `booktracker-kv-<hash>-pe` | `australiaeast` | in primary `private-endpoints` subnet |
| Azure OpenAI account | `booktracker-openai-<hash>` | `eastus2` | S0; `publicNetworkAccess = Disabled`; `gpt-4o` deployment, Standard SKU, capacity 10 |
| Azure OpenAI Private Endpoint | `booktracker-openai-<hash>-pe` | `eastus2` | in secondary `private-endpoints` subnet |
| Private DNS zones | `privatelink.database.windows.net`, `privatelink.vaultcore.azure.net`, `privatelink.openai.azure.com` | global | each linked to both VNets so the App resolves PE IPs |
| Log Analytics workspace | `booktracker-logs` | `australiaeast` | 30-day retention |
| Application Insights | `booktracker-ai` | `australiaeast` | workspace-based, wired to the App Service |

The App Service uses **Easy Auth v2** pointed at the `Library-Patrons` Entra app registration. With `appRoleAssignmentRequired = true` set on its service principal, only users/groups assigned to the enterprise app can sign in.

### Why eastus2

The Azure OpenAI account lives in `eastus2`, not `australiaeast`. Microsoft is retiring `gpt-4o` in `australiaeast` on 2026-06-03 with no announced successor, so a second region is required regardless. The eastus2 VNet exists solely to host the OpenAI PE; cross-region traffic from the App Service flows through the VNet peering.

Microsoft Foundry (Claude on Azure) is **not provisioned** — Drew's subscription is `Sponsored_2016-01-01`, which Microsoft excludes from Claude eligibility on Foundry. The `MicrosoftFoundry` provider therefore won't appear in the app's runtime picker; direct Anthropic API (public, no Azure resource) remains the way to reach Claude. See `TODO.md` for the follow-up to add Foundry once on an EA / MCA-E subscription.

## Network topology

```
Internet → App Service (australiaeast, public, Easy Auth)
              │ VNet integration (vnetRouteAllEnabled)
              ▼
       primary VNet (10.0.0.0/16, australiaeast)
           ├── PE → SQL (australiaeast)
           └── PE → Key Vault (australiaeast)
              │ peering
              ▼
       secondary VNet (10.1.0.0/16, eastus2)
           └── PE → Azure OpenAI (eastus2)
```

Each Private DNS Zone is linked to both VNets so the App Service (resolving via the australiaeast VNet) gets the private IP regardless of which side of the peer hosts the resource.

## Secrets and Key Vault references

All secret App Settings resolve via `@Microsoft.KeyVault(SecretUri=…)` references:

| App setting | KV secret | Source |
|---|---|---|
| `MICROSOFT_PROVIDER_AUTHENTICATION_SECRET` | `AuthClientSecret` | rotated by `deploy.ps1` (2-year expiry) |
| `AI__AzureOpenAI__ApiKey` | `AIAzureOpenAIApiKey` | written by `ai-services.bicep` via `listKeys()` — never seen by the deploy script |
| `AI__Anthropic__ApiKey` | `AIAnthropicApiKey` | optional; supplied via `-AnthropicApiKey` |
| `Trove__ApiKey` | `TroveApiKey` | optional; supplied via `-TroveApiKey` |

App Service caches resolved KV values; rotating a secret takes effect on the next reference refresh (~24h, or immediate via portal "Refresh Key Vault references").

**KV references are always emitted.** Every deploy writes the four KV-reference app settings above regardless of whether `-AnthropicApiKey` / `-TroveApiKey` is supplied on that run. The reference is stable; the *secret* is only written to Key Vault when the corresponding key param is non-empty. A deploy without the key therefore leaves the existing secret (and reference resolution) untouched. An un-provisioned secret resolves as empty to the app, which silently drops the provider from the picker.

### Slot-sticky settings

The following app settings are marked `slotConfigNames.appSettingNames` so they stay pinned to their slot during a swap (rather than moving with the code):

- `ASPNETCORE_ENVIRONMENT`
- `MICROSOFT_PROVIDER_AUTHENTICATION_SECRET`
- `AI__Anthropic__ApiKey`
- `AI__AzureOpenAI__ApiKey`
- `AI__AzureOpenAI__Endpoint`
- `AI__AzureOpenAI__Deployment`
- `AI__DefaultProvider`
- `Trove__ApiKey`

The connection string `DefaultConnection` is also slot-sticky (`slotConfigNames.connectionStringNames`). Prod points at `booktracker`, staging points at `booktracker-staging`; without the pin a swap would land the formerly-staging code on the prod URL still pointing at the staging DB, an immediate prod outage.

Reason: swap-then-redeploy-without-the-key previously let the Anthropic key drift between slots and eventually get lost. Making keys and AI config slot-bound means swaps are purely code-shaped — secrets, environment, and database all stay where they were configured.

### Staging is a real environment now

Prod and staging hit **separate databases** (`booktracker` vs `booktracker-staging` on the same SQL server). Each slot's managed identity is granted only on its own DB. Implications:

- Schema migrations are tested on staging *before* they hit prod data — the `swap.yml` slot-swap is now a real rollback story for code, not a fictional one against a shared DB.
- Staging starts **empty** on first deploy of this change; migrate-on-startup builds the schema. Empty staging catches schema syntax errors but **misses every data-shape failure** (NOT NULL with existing nulls, unique-index dedup violations, FK orphans, type-conversion failures, performance-on-real-data). For migrations tagged `review:` the safe pattern is to refresh staging from prod first — see `TODO.md` for the bacpac-sync follow-up.

### Order of operations after this lands

The first time this Bicep runs against an existing deployment:

1. **Run `deploy.ps1`** — Bicep creates `booktracker-staging`, repoints the staging slot's connection string at it, marks the CS slot-sticky, and `deploy.ps1` grants the staging managed identity on the new DB (and drops the orphan staging-identity grant from the prod DB).
2. **Redeploy the app** to staging (push to `main`, or restart the staging slot) so migrate-on-startup runs against the empty staging DB and creates the schema.
3. **Verify staging URL** loads — empty library, but the app should be alive.
4. Prod is unaffected throughout — the prod slot's CS still points at the prod DB.

## Prereqs

- PowerShell 7+ on Windows.
- An Azure subscription where you have Owner (or Contributor + User Access Administrator).
- An Entra tenant where you can create app registrations (`Application.ReadWrite.All` + `Directory.Read.All` on the signed-in user is sufficient).
- You must run `deploy.ps1` as a **user** (not a service principal) — the script sets the signed-in user as the SQL AAD admin.

Required PowerShell modules (auto-installed to `CurrentUser` on first run): `Az.Accounts`, `Az.Resources`, `Microsoft.Graph.Applications`, `Microsoft.Graph.Users`, `SqlServer` (v22+ for AAD token auth).

## Deploy

```powershell
cd infra
./deploy.ps1 -TenantId '<tenant-guid>' -SubscriptionId '<sub-guid>'
```

Optional parameters:

| Param | Default | Purpose |
|---|---|---|
| `-Location` | `australiaeast` | primary region |
| `-AppName` | `booktracker` | base name for resources |
| `-EnterpriseAppName` | `Library-Patrons` | enterprise app name for Easy Auth |
| `-CustomDomain` | _empty_ | bind a custom hostname (e.g. `books.silly.ninja`) — see below |
| `-SecondaryLocation` | `eastus2` | region for the AI VNet + OpenAI account |
| `-DevClientIp` | _empty_ | optional IPv4 to whitelist on the SQL firewall for ad-hoc local EF migrations; leave blank to keep SQL fully private |
| `-AnthropicApiKey` | _empty_ | optional `sk-ant-…`; when supplied it's stored in Key Vault and exposed via a KV ref |
| `-TroveApiKey` | _empty_ | optional NLA Trove API key; when supplied it's stored in Key Vault and exposed via a KV ref |

What the script does:
1. Signs you in to Azure + Microsoft Graph.
2. Creates the `Library-Patrons` app registration + service principal if missing; sets `appRoleAssignmentRequired = true`.
3. Rotates a 2-year client secret for Easy Auth.
4. Runs `main.bicep` at subscription scope (creates the RG + everything else).
5. Registers the App Service's Easy Auth callback URL on the app registration.
6. Temporarily flips SQL `publicNetworkAccess` on, opens a temp firewall rule for the script's public IP, connects with AAD auth, grants the prod managed identity `db_datareader/writer/ddladmin` on the prod DB and the staging managed identity the same on the staging DB (each identity only sees its own DB; the prod-DB grant also drops any orphan staging-identity grant from a pre-split deploy), then restores SQL back to private.

### Local EF migrations after the cutover

With SQL on a Private Endpoint by default, `dotnet ef database update` from a developer laptop won't reach the server. Two options:

- Re-run `deploy.ps1` with `-DevClientIp <your.ipv4>`. This both creates a `DevClient` firewall rule and flips `publicNetworkAccess` to `Enabled`. Re-run without the flag later to seal it back up.
- Or let CI run migrations (the existing on-startup `MigrateAsync` path still works since the App Service hits SQL through the PE).

### Refresh the local dev DB with a copy of prod

For realistic local testing (dedup UI, bulk scans, AI features against real data) the local Docker SQL Server can be refreshed from prod via BACPAC:

```powershell
./infra/refresh-local-db.ps1 -TenantId '<tenant-guid>' -SubscriptionId '<sub-guid>'
```

What it does:

1. Signs in to Azure, locates the prod SQL server (`booktracker-sql-<suffix>` in `rg-booktracker-prod`).
2. Temporarily enables SQL public network access + opens a firewall rule for the caller's IP (same pattern `deploy.ps1` uses).
3. Runs `SqlPackage /a:Export` with AAD auth; lands the `.bacpac` in `./artifacts/` (gitignored).
4. Restores the firewall state in `finally` even if the export aborts.
5. Prompts before clobbering the local DB (skip with `-Force` on re-runs).
6. Drops the local `BookTracker` DB and `SqlPackage /a:Import`s the BACPAC into the `booktracker-db` container on `localhost:1433`.

Prereqs:

- **SqlPackage on PATH.** Preferred install is the .NET global tool:

  ```powershell
  dotnet tool install -g microsoft.sqlpackage
  ```

  (Alternatives: Azure Data Studio's "SQL Database Projects" extension, or the standalone installer at `https://aka.ms/sqlpackage-windows`.) Open a fresh shell afterwards so the updated PATH is picked up.
- Docker running with `docker compose up -d` already applied.
- `Az.Accounts`, `Az.Resources`, `Az.Sql`, and `SqlServer` modules — auto-install on first run.

Direction: **prod → local only**. There is no reverse path — data flows into prod through normal app usage, never this script. If this branch has migrations not yet applied to prod, run `dotnet ef database update --project .\BookTracker.Data --startup-project .\BookTracker.Web` after the import.

Flags: `-SkipExport` reuses the most recent BACPAC in `./artifacts/` (handy when iterating on import). `-SkipImport` just downloads the BACPAC.

## GitHub Actions CI/CD

After the first `deploy.ps1` run, set up the GitHub → Azure OIDC link:

```powershell
./setup-github-oidc.ps1 -TenantId '<tenant>' -SubscriptionId '<sub>' `
    -GitHubOrg 'N3rdage' -GitHubRepo 'the-library'
```

The script creates a `booktracker-ci` app registration with federated identity credentials (one for pushes to `main`, one for pull requests), assigns it Contributor on `rg-booktracker-prod`, and prints six GitHub repository variables for you to configure at `Settings → Secrets and variables → Actions → Variables`.

The script also grants the CI SP the narrow permissions needed by the scheduled Easy Auth secret rotation:
- **Key Vault Secrets Officer** on the single KV in the RG (write access to `AuthClientSecret`).
- **Owner** of the `Library-Patrons` app registration (so it can rotate passwords on that specific app reg).
- **Microsoft Graph `Application.ReadWrite.OwnedBy`** as an app role (narrow — only apps the SP owns). This is the "admin consent" — the user running the script needs `Application.ReadWrite.All` or Global Admin to issue it.

The KV role assignment and app-reg ownership require the KV + app reg to already exist. If you run `setup-github-oidc.ps1` before `deploy.ps1`, those steps are skipped with a warning; re-run it after the first deploy to fill them in.

Workflows under `.github/workflows/`:
- `ci.yml` — build on PRs.
- `deploy.yml` — on push to `main`: build, publish, deploy to the **staging** slot.
- `swap.yml` — manual: `az webapp deployment slot swap staging -> production`. (Adding a GitHub Environment with required reviewers is tracked in `TODO.md`.)
- `rotate-easy-auth-secret.yml` — cron (twice yearly, 1st of every 6th month at 02:00 UTC) + manual dispatch: generates a new password on the `Library-Patrons` app registration, writes it to KV, trims old passwords to keep the latest 2. App Service picks up the new secret via the KV reference within ~24h. Manual dispatch: `gh workflow run rotate-easy-auth-secret.yml`.

Schema migrations currently run on app startup via `db.Database.MigrateAsync()`. Fine for a single-instance app; switching to a deploy-time migration bundle is tracked in `TODO.md`.

## Post-deploy

Assign users/groups to the enterprise app so they can sign in:

`https://entra.microsoft.com` → **Identity** → **Applications** → **Enterprise applications** → **Library-Patrons** → **Users and groups** → **Add user/group**.

Unassigned users attempting to sign in will see `AADSTS501051`.

## Custom domain

Bind a custom hostname (e.g. `books.silly.ninja`) to the production slot with a free App Service Managed Certificate:

1. Run `./deploy.ps1 -TenantId … -SubscriptionId …` **without** `-CustomDomain`. The last block of output shows the `asuid.<subdomain>` TXT value and the CNAME target.
2. At your DNS host (Gandi in our case), add:
   - `TXT  asuid.books  <customDomainVerificationId>`
   - `CNAME books  <app>.azurewebsites.net`
3. Wait for DNS to propagate (a few minutes to an hour; `nslookup books.silly.ninja` should resolve).
4. Re-run `./deploy.ps1 -TenantId … -SubscriptionId … -CustomDomain books.silly.ninja`. Bicep adds the hostname binding, issues the managed cert, and binds SSL. The script also registers the custom-domain redirect URI on the `Library-Patrons` app registration.

Works for CNAME-pointed subdomains only. Apex domains (`silly.ninja`) need a different cert issuance path (A record + ALIAS or equivalent) — not covered here.

## AI provider configuration

The app supports up to three AI providers; only providers with valid config appear in the runtime toggle. App Settings are wired by `app-config.bicep` and resolve secrets from Key Vault automatically — there's no portal step for the provisioned providers.

### Anthropic (direct, public API)

The Anthropic provider hits `api.anthropic.com` directly (no Azure resource needed). To enable it, supply the key on a deploy:

```powershell
./deploy.ps1 -TenantId … -SubscriptionId … -AnthropicApiKey 'sk-ant-…'
```

The script passes the key to Bicep, which writes it to Key Vault as `AIAnthropicApiKey`. The `AI__Anthropic__ApiKey` App Setting becomes a KV reference. Rotate later by re-running the deploy with a new key.

## ISBN lookup providers

`BookLookupService` tries Open Library first, then Google Books. Both are keyless and zero-config. A third provider, **Trove** (National Library of Australia), sits at the end of the chain and specifically catches self-published / Australian titles the other two tend to miss. Trove requires a free API key.

### Trove (optional)

Register at `https://trove.nla.gov.au/about/create-something/using-api` — approvals can take a few days. Once you have the key, provision it the same way as the Anthropic key:

```powershell
./deploy.ps1 -TenantId … -SubscriptionId … -TroveApiKey '<your-key>'
```

The script writes it to Key Vault as `TroveApiKey`; the `Trove__ApiKey` App Setting becomes a KV reference. Without a key, the Trove branch of the lookup chain is skipped silently — Open Library and Google Books continue to work as before.

### Azure OpenAI (gpt-4o, eastus2)

Provisioned automatically by `ai-services.bicep` — no manual portal step, no key handling. The Bicep:
- Creates the `booktracker-openai-<hash>` account in eastus2 with `publicNetworkAccess = Disabled`.
- Deploys `gpt-4o` (version `2024-11-20`, Standard, capacity 10).
- Writes `key1` into KV as `AIAzureOpenAIApiKey` via `listKeys()` (the deploy script never sees it).
- Adds a Private Endpoint in the eastus2 VNet so the App Service can reach it.
- Grants both App Service slot identities the `Cognitive Services User` role on the account so a future managed-identity migration is just a code change.

### Microsoft Foundry (deferred)

Not provisioned. See "Why eastus2" above and `TODO.md`.

### Local development

For local dev, copy `appsettings.Example.json` → `appsettings.Development.json` and fill in the `AI:` section with the providers you want to test (using `:` instead of `__` as the separator). Local dev hits the Azure resources only if you supply real endpoints/keys; otherwise stub or skip.
