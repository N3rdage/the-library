# Azure infrastructure

Provisions the Azure footprint for BookTracker into a single resource group.

## What gets created

All tagged with `Client = Drew` and `Environment = Production`.

| Resource | Name pattern | Notes |
|---|---|---|
| Resource group | `rg-booktracker-prod` | holds everything; tags inherited |
| App Service plan | `booktracker-plan` | Linux, S1 |
| App Service | `booktracker-<hash>` | Linux, DOTNETCORE\|10.0, WebSockets + AlwaysOn + ARR Affinity on |
| System-assigned managed identity | on the App Service | granted `db_datareader/writer/ddladmin` on the SQL DB |
| Azure SQL logical server | `booktracker-sql-<hash>` | **AAD-only auth**; the user running `deploy.ps1` becomes the AAD admin |
| Azure SQL database | `booktracker` | Basic tier, 5 DTU |
| Log Analytics workspace | `booktracker-logs` | 30-day retention |
| Application Insights | `booktracker-ai` | workspace-based, wired to the App Service |

The App Service uses **Easy Auth v2** pointed at the `Library-Patrons` Entra app registration. With `appRoleAssignmentRequired = true` set on its service principal, only users/groups assigned to the enterprise app can sign in.

## Prereqs

- PowerShell 7+ on Windows.
- An Azure subscription where you have Owner (or Contributor + User Access Administrator).
- An Entra tenant where you can create app registrations (global admin consent is not required; `Application.ReadWrite.All` + `Directory.Read.All` on the signed-in user is).
- You must run `deploy.ps1` as a **user** (not a service principal) — the script sets the signed-in user as the SQL AAD admin.

Required PowerShell modules (auto-installed to `CurrentUser` on first run): `Az.Accounts`, `Az.Resources`, `Microsoft.Graph.Applications`, `Microsoft.Graph.Users`, `SqlServer` (v22+ for AAD token auth).

## Deploy

```powershell
cd infra
./deploy.ps1 -TenantId '<tenant-guid>' -SubscriptionId '<sub-guid>'
```

Optional parameters: `-Location` (default `australiaeast`), `-AppName` (default `booktracker`), `-EnterpriseAppName` (default `Library-Patrons`).

What the script does:
1. Signs you in to Azure + Microsoft Graph.
2. Creates the `Library-Patrons` app registration + service principal if missing; sets `appRoleAssignmentRequired = true`.
3. Rotates a 2-year client secret for Easy Auth.
4. Runs `main.bicep` at subscription scope (creates the RG + everything else).
5. Registers the App Service's Easy Auth callback URL on the app registration.
6. Connects to the new SQL DB with AAD auth and grants the App Service managed identity `db_datareader/writer/ddladmin`.

## GitHub Actions CI/CD

After the first `deploy.ps1` run, set up the GitHub → Azure OIDC link:

```powershell
./setup-github-oidc.ps1 -TenantId '<tenant>' -SubscriptionId '<sub>' `
    -GitHubOrg 'N3rdage' -GitHubRepo 'the-library'
```

The script creates a `booktracker-ci` app registration with federated identity credentials (one for pushes to `main`, one for pull requests), assigns it Contributor on `rg-booktracker-prod`, and prints six GitHub repository variables for you to configure at `Settings → Secrets and variables → Actions → Variables`.

Workflows under `.github/workflows/`:
- `ci.yml` — build on PRs.
- `deploy.yml` — on push to `main`: build, publish, deploy to the **staging** slot.
- `swap.yml` — manual: `az webapp deployment slot swap staging -> production`. TODO in the file to wire up a GitHub Environment with required reviewers.

Schema migrations currently run on app startup via `db.Database.MigrateAsync()`. Fine for a single-instance app; there's a TODO in `Program.cs` to switch to a deploy-time migration bundle once the app scales out.

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

The app supports three AI providers. Configure whichever ones you want to use — only providers with API keys will appear in the runtime toggle. Set these as App Settings in the Azure Portal (`Configuration > Application settings`) or pass them via Bicep parameters.

### Anthropic (direct API)

1. Create an account at [console.anthropic.com](https://console.anthropic.com).
2. Go to **API Keys** and create a new key.
3. Add the app setting:

| Setting | Value |
|---------|-------|
| `AI__DefaultProvider` | `Anthropic` |
| `AI__Anthropic__ApiKey` | `sk-ant-...` (your API key) |

Model defaults (Sonnet for fast ops, Opus for deep analysis) are built into the app — no need to configure them unless you want to override.

### Azure AI Foundry (Claude via Azure)

1. In the [Azure Portal](https://portal.azure.com), create an **Azure AI Foundry** resource (or use an existing one).
2. Deploy a Claude model (e.g. `claude-sonnet`) — note the deployment name.
3. Optionally deploy a second model for deep analysis (e.g. `claude-opus`).
4. From the resource's **Keys and Endpoint** page, copy the endpoint URL and a key.
5. Add the app settings:

| Setting | Value |
|---------|-------|
| `AI__DefaultProvider` | `AzureFoundry` (or keep `Anthropic` and switch at runtime) |
| `AI__AzureFoundry__Endpoint` | `https://<resource>.services.ai.azure.com` |
| `AI__AzureFoundry__ApiKey` | Your Azure AI Foundry key |
| `AI__AzureFoundry__FastDeployment` | Deployment name for fast ops (e.g. `claude-sonnet`) |
| `AI__AzureFoundry__DeepDeployment` | Deployment name for deep analysis (e.g. `claude-opus`) |

### Azure OpenAI (GPT-4o)

1. In the [Azure Portal](https://portal.azure.com), create an **Azure OpenAI** resource.
2. Go to **Azure AI Foundry** (linked from the resource) and deploy a model — e.g. `gpt-4o`. Note the deployment name.
3. From the resource's **Keys and Endpoint** page, copy the endpoint URL and a key.
4. Add the app settings:

| Setting | Value |
|---------|-------|
| `AI__DefaultProvider` | `AzureOpenAI` (or keep another default and switch at runtime) |
| `AI__AzureOpenAI__Endpoint` | `https://<resource>.openai.azure.com` |
| `AI__AzureOpenAI__ApiKey` | Your Azure OpenAI key |
| `AI__AzureOpenAI__Deployment` | Deployment name (e.g. `gpt-4o`) |

### Notes

- You can configure multiple providers simultaneously. The app auto-detects which ones have valid keys and shows them in the provider toggle dropdown.
- `AI__DefaultProvider` determines which provider is active on page load. Users can switch at runtime via the dropdown on the AI Assistant and Bulk Add pages.
- For local development, add the same settings to `appsettings.Development.json` under the `AI` section (using `:` instead of `__` as the separator). See `appsettings.Example.json` for the structure.

## TODOs

- Move the Easy Auth client secret from an app setting to a Key Vault reference and schedule rotation.
- Consider Private Endpoint + VNet integration instead of the "Allow Azure services" SQL firewall rule.
- Add a staging slot to the App Service once the deploy pipeline is in place.
- Consider moving AI API keys to Key Vault references for better secret management.
