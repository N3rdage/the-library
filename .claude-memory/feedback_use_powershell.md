---
name: Use PowerShell tool, not Bash
description: For all shell operations on this project, use the PowerShell tool. The harness exposes both Bash and PowerShell, and CLAUDE.md mandates PowerShell — Drew is on Windows + PowerShell 7 and wants the tooling to match what he uses locally.
type: feedback
---

When this project's CLAUDE.md needs a shell, use the **PowerShell** tool — not Bash. Even though the harness's environment block lists `Shell: bash` as available, CLAUDE.md's "Environment" section explicitly mandates Windows shell:

> *"Windows only. All commands in this file and any you suggest must target a Windows shell (PowerShell 7 / Windows PowerShell / cmd.exe). Do not use Unix-isms like `/dev/null`, forward-slash-only paths, `export VAR=…`, `&&` chaining habits from bash scripts, or POSIX tools (`grep`, `sed`, `cat`) in instructions."*

This applies to **how I invoke shell commands**, not just suggestions in chat. Reach for the PowerShell tool by default for git operations, build runs, file checks, etc. Bash works in WSL but produces output that's harder to translate into commands Drew can re-run on his side, and the inconsistency adds friction during incidents.

**Why:** Drew works in PowerShell 7 on Windows; using PowerShell as the tooling layer means his copy-paste of any command I run lands cleanly in his shell, the project's existing `.ps1` scripts are first-class, and the conversation stays internally consistent. Asked explicitly on 2026-04-30 after noticing Bash tool use through prior sessions.

**How to apply:**

- Default to the PowerShell tool for any shell command. Use Bash only if a specific operation genuinely needs Unix-only tooling (rare on this project — none come to mind).
- When suggesting commands in chat output for Drew to run, write them in PowerShell syntax: `;` chaining or `&&`/`||` (PowerShell 7 supports both), `$env:VAR = "..."` for env vars, `Out-Null` instead of `> /dev/null`, `Get-Content` / `Select-String` instead of `cat` / `grep`.
- Quote paths with spaces using double quotes: `"C:\Users\Drew.Work\code\The Library\..."`.
- Multi-line strings via single-quoted here-strings (`@'…'@`) for commit messages and other content with `$` characters that shouldn't be interpolated.
- Dedicated tools (Read, Write, Edit, Glob, Grep) are still preferred over shelling out — this rule is about *which shell* to use when shelling is the right move, not about replacing the dedicated tools.
