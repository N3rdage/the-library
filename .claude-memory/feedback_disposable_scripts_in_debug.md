---
name: disposable-scripts-in-debug
description: "One-off data cleanups and disposable scripts go in .debug/ (gitignored) — don't create a tracked csproj for them"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 65621c02-8a85-4481-8acc-1e75e4f9c385
---

When the task is a **one-off data cleanup or other disposable mutation**, drop a single `.debug/<name>.ps1` script (the `.debug/` dir is gitignored — see .gitignore line 27). Do **not** spin up a new `BookTracker.Tools.*` csproj for it.

**Why:** Drew pushed back 2026-05-21 when I proposed a full `BookTracker.Tools.BackfillDestroyerGenres` console app + driver `.ps1` for the Sapir/Destroyer genre backfill (S1 of the dogfood data cleanup): *"am a bit unsure about creating a full project for a once off data cleanup, can we maybe just do a ps1 file in .debug as it will be disposable"*. A tracked csproj is a long-term artefact — solution-file entry, CI compile cost, future-maintenance signal. A backfill that runs once and never again doesn't earn that.

**How to apply:**
- **Disposable mutations → `.debug/*.ps1`.** No csproj, no branch needed (`.debug/` is gitignored so there's nothing to commit). Connect via Az PowerShell session + Microsoft.Data.SqlClient or `Invoke-Sqlcmd` for SQL.
- **Recurring or reusable tooling → `BookTracker.Tools.*` csproj.** [[snapshot-dump-cli]] (PR #270) earns the csproj because it runs every time Drew refreshes the Claude Analysis Project — recurring use.
- **Litmus test before suggesting a csproj:** would this code run more than 2-3 times? If no, ps1-in-.debug. The SnapshotDump pattern is for recurring read-only ops, not one-shot writes.

Related: [[feedback_dogfood_data_cleanup]] (script vs UI choice — this feedback is the *shape* of the script when it IS scripted), [[user_drew]] (solo dev, doesn't want code-debt from one-off tasks).
