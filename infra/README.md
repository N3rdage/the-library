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

## TODOs

- Move the Easy Auth client secret from an app setting to a Key Vault reference and schedule rotation.
- Consider Private Endpoint + VNet integration instead of the "Allow Azure services" SQL firewall rule.
- Add a staging slot to the App Service once the deploy pipeline is in place.
