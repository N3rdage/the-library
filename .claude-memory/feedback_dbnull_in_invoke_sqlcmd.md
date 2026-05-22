---
name: dbnull-in-invoke-sqlcmd
description: "Invoke-Sqlcmd returns [System.DBNull] for SQL NULL columns; DBNull is truthy in PowerShell, so the naive `if ($x)` check passes through and silently produces empty strings via [string]$null."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 14ead535-c1be-4433-a971-1baced3164cd
---

When a `.debug/*.ps1` script queries the BookTracker DB via `Invoke-Sqlcmd` (or any ADO.NET path that returns a `DataRow`), columns that contain SQL `NULL` come back as `[System.DBNull]::Value`. Two PowerShell-specific gotchas compound:

1. **DBNull is truthy.** `if ($row.SomeCol)` passes through even when the column is NULL, because DBNull is a non-null object reference.
2. **Casting DBNull to string yields the empty string.** `[string]$row.SomeCol` returns `""`, not `$null` and not the type name.

Combined effect: the natural-looking `if ($row.Col -and -not [string]::IsNullOrWhiteSpace([string]$row.Col)) { ... } else { '(missing)' }` quietly takes the truthy branch and emits an empty string with no fallback. This bit me 2026-05-22 generating `noisy-genre-works.md` — Works with no author rows rendered as `_  ` (open-italic, empty content, close-italic) instead of `(no author!)`.

**Fix shape:** check `-is [System.DBNull]` explicitly *before* the string coercion:

```powershell
$value = if ($row.Col -is [System.DBNull]) { '' } else { [string]$row.Col }
# now $value is safe to feed to IsNullOrWhiteSpace, string interpolation, etc.
```

Apply this to every nullable column the report touches — not just the obviously-nullable ones. `LEFT JOIN` results, `STRING_AGG` over empty sets, and any column the schema allows NULL on need the guard.

Related: [[disposable-scripts-in-debug]] (the `.ps1`-in-.debug shape these scripts live in), [[runbook-prod-sql-access]] (the AAD-auth `Invoke-Sqlcmd` path against prod, same DBNull surface).
