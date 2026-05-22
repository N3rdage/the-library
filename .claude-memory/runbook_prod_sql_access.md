---
name: runbook-prod-sql-access
description: Prod Azure SQL access needs a temp firewall rule + Entra auth — never plain SQL auth. Pattern lives in infra/refresh-local-db.ps1.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 14ead535-c1be-4433-a971-1baced3164cd
---

Connecting to the prod (or staging) Azure SQL database from Drew's laptop has two non-obvious gates that local-Docker SQL doesn't have:

1. **Public network access is normally OFF** on the SQL server. A run from his laptop needs `Set-AzSqlServer -PublicNetworkAccess 'Enabled'` first, then restore after.
2. **The server's IP firewall rejects everything by default.** A temporary firewall rule for the caller's current IP must be added before the connection works, and removed after.
3. **Auth is Entra-only.** SQL auth is disabled — every query needs an Azure AD token. Conn string form: `Authentication=Active Directory Default` (after `Connect-AzAccount` or `az login`), OR explicit token via `Get-AzAccessToken -ResourceUrl 'https://database.windows.net'` + `Invoke-Sqlcmd -AccessToken`.

**Canonical implementation lives in [`infra/refresh-local-db.ps1`](../../../code/The Library/infra/refresh-local-db.ps1) — lines ~135–185 wrap the whole dance (token fetch → public-access toggle → temp rule with ipify-detected IP and guid-suffix name → 10s propagation wait → query → cleanup in `finally`).** When a `.debug/` cleanup needs prod access, do not reinvent — either reuse that pattern inline, or have Drew run a one-time `Connect-AzAccount` + manual `New-AzSqlServerFirewallRule` + manual public-access toggle, then pass the AD conn string to the cleanup script.

**Short AD conn string form** (assuming a logged-in Az session):
```
Server=tcp:<server>.database.windows.net,1433;Database=<db>;Authentication=Active Directory Default;Encrypt=True
```

`.debug/data-fixes/converge-destroyer-genres.ps1` accepts `-Server <name> -Database <name>` and builds this string internally so prod invocations stay short — the firewall/public-access ritual is **not** baked in; do that separately (or refactor `refresh-local-db.ps1`'s helper out if the pattern proliferates).

**Gotchas:**
- `New-AzSqlServerFirewallRule` returns success before the rule has fully propagated — `refresh-local-db.ps1` sleeps 10s after the add, do the same.
- `Get-AzAccessToken` in Az PowerShell ≥12 returns `-AsSecureString` by default; unwrap with `ConvertFrom-SecureString -AsPlainText` before passing to SqlPackage / Invoke-Sqlcmd.
- `Authentication=Active Directory Default` uses DefaultAzureCredential's chain: env vars → managed identity → VS Code → Azure CLI → Az PowerShell. As long as **any** of those is logged in, it works — `Connect-AzAccount` (Az PS) and `az login` (Azure CLI) both satisfy the chain.
- Public network access toggle has tenant-wide audit logging. Don't leave it Enabled — restore in a `finally`.

Related: [[disposable-scripts-in-debug]] (the shape — `.ps1` not csproj), [[dogfood-data-cleanup]] (when to script vs UI).
