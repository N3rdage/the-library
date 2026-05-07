---
name: Avoid encoded PowerShell command-line args
description: PowerShell here-strings, multi-line bodies, AND single-statement commands with long URLs get serialised as `-EncodedCommand <base64>` by the tool layer, which Drew's security tooling flags as a malware indicator. The trigger is "long enough or complex enough that the harness encodes it" — *length alone is sufficient*, even for one-liners. Applies to any PowerShell call, not just commit messages.
type: feedback
originSessionId: 21e97fa1-cba4-4b67-9d43-dc109098d6b2
---
Long OR multi-statement inline arguments to PowerShell get serialised by the tool layer as `powershell.exe -EncodedCommand <base64-blob>`. Security tooling on Windows flags base64 command-line args as a malware indicator (legitimate use; it's a common obfuscation pattern). Drew has surfaced this triggering during sessions.

**Decision rule before calling PowerShell:** if the command would span multiple visual lines, contains `if/else`, multiple statements joined with `;`, here-string `@'...'@`, a multi-step pipeline with quoted file paths and variables, OR a single long URL with query string in an `Invoke-WebRequest` / `Invoke-RestMethod` / `curl` argument — assume the harness will encode it. Reach for a temp file, split into separate calls, or have the user run the command in their own terminal instead. The trigger isn't "commit message" or even "multi-statement" specifically; it's **"this looks long when you write it out, however it got long."**

**Why:** the encoded-command form is the standard way long PowerShell args get passed through tool boundaries safely; it's not malicious, but it's indistinguishable from malware on the wire to a scanner. The flag is reliable, not a false positive in the strict sense — the encoded form is what the scanner is configured to flag.

**How to apply:** for any command that would otherwise need a multi-line here-string or a long inline argument, in priority order:

- **Temp file (the reliable default).** Write the message to a file in `$env:TEMP` (e.g. `$env:TEMP\bt-commit-msg.txt`) via the Write tool, then run a short `git commit -F "$env:TEMP\bt-commit-msg.txt"` (PowerShell) or `git commit -F "$LOCALAPPDATA/Temp/bt-commit-msg.txt"` (Bash on Windows), then delete the file. The long string never crosses any shell command line, so no encoded-command path can be triggered. **Do NOT put scratch files in `.git/`** — that directory shouldn't be a write target for agentic tools because it's a privilege-escalation surface (write access to `.git/hooks/` is RCE-equivalent on the user's next git operation; write access to `.git/config` lets you redirect `remote.origin.url` so the user's next push lands on an attacker mirror). The OS temp dir is outside the repo, has no security sensitivity, and is the kind of write target a security-aware user can grant once globally.
- **Short inline `-m`.** Keep commit messages to a single line short enough that PowerShell can pass them as a normal arg. Reasonable for tight bug fixes; not for substantial features.
- **Bash heredoc — does NOT work for git on Windows.** `git commit -F /dev/stdin <<'EOF' ... EOF` fails with `fatal: could not read log file '/proc/self/fd/0'` because Git for Windows is a Windows-native binary that can't open POSIX paths. Don't reach for this; jump straight to the temp-file approach. (For non-git commands that read stdin natively, Bash heredoc may still work — but git is the common case where this comes up, and it doesn't.)

**Specifically NOT — the patterns that have actually tripped this:**

- `git commit -m @'multi-line here-string'@` via PowerShell — the original trigger.
- Long multi-line PR body text inline to `gh pr create` via PowerShell — same shape. Use `--body-file <path>` instead.
- **Multi-statement PowerShell smoke checks** like `dotnet build ...; $dll = "..."; if (Test-Path $dll) { $bytes = ...; $asm = [...]::Load($bytes); ... } else { ... }` — exactly the shape that re-triggered the AV during the BuildInfo work. If you need to do load-and-inspect, write the inspection script to a `.ps1` file via the Write tool and run that file, OR split into separate one-liner Bash/PowerShell calls, OR skip the smoke check if unit tests already cover the logic.
- Any `powershell -Command "..."` with embedded newlines or multiple statements.
- **Single-statement `Invoke-WebRequest` or `Invoke-RestMethod` with a long URL.** Surfaced 2026-05-08 during the multi-work-collections PR2 API investigation: even a one-liner `Invoke-WebRequest "https://openlibrary.org/api/books?bibkeys=ISBN:...&format=json&jscmd=data" -OutFile "$env:TEMP\..."` got encoded because the URL itself was long. Splitting one batched URL into 6 short individual `iwr` calls *also* tripped the AV when run as a parallel batch through the harness — the cumulative-or-each-too-long heuristic the wrapper uses isn't transparent. Final-fallback: drop down to the **user-runs-it** layer below.

**Final fallback when even a short single-statement call is getting encoded — user-runs-it.** Some commands are unavoidably long (long URLs, gathering output from multiple endpoints into multiple files for the agent to read). When the harness keeps encoding them no matter how I split them up:

1. Write the script as a paste-able multi-line block in chat (PowerShell, with comments, easy to inspect before running).
2. Ask the user to paste it into their own PowerShell terminal — *their* shell doesn't go through the encoding wrapper, so there's nothing for AV to flag.
3. Have the script write its outputs to `$env:TEMP\bt-*.json` (or similar) — predictable filenames I can `Read` afterwards.
4. User says "ran" / "done"; I read the files and continue.

Worked cleanly on the 2026-05-08 API investigation: gave Drew a `foreach` loop hitting 12 endpoints, he ran it once in his terminal, all 12 JSON files landed in `$env:TEMP`, I `Read` each. No AV trips, no harness friction. The downside is loss of agent autonomy for that one step — but the security model is intact and the workaround is cheap.

Temp-file is still the durable answer for commit messages and for self-driven file output (where I write the file and consume it without harness round-tripping). User-runs-it is the layer above for "even single short calls keep getting encoded." The standing convention is PowerShell-first per `feedback_use_powershell.md`; these rules layer underneath it for the cases where the harness encoding gets in the way.
