---
name: Avoid encoded PowerShell command-line args
description: PowerShell here-strings and other long inline args get serialised as `-EncodedCommand <base64>` by the tool layer, which Drew's security tooling flags as a malware indicator. Prefer file-based or short inline alternatives for long commands (especially git commit messages).
type: feedback
originSessionId: 21e97fa1-cba4-4b67-9d43-dc109098d6b2
---
Long inline arguments to PowerShell — multi-line here-strings (`@'...'@`), big multi-line `-Command` payloads, or `git commit -m` with a long here-string body — are serialised by the tool layer as `powershell.exe -EncodedCommand <base64-blob>`. Security tooling on Windows flags base64 command-line args as a malware indicator (legitimate use; it's a common obfuscation pattern). Drew has surfaced this triggering during a session.

**Why:** the encoded-command form is the standard way long PowerShell args get passed through tool boundaries safely; it's not malicious, but it's indistinguishable from malware on the wire to a scanner. The flag is reliable, not a false positive in the strict sense — the encoded form is what the scanner is configured to flag.

**How to apply:** for any command that would otherwise need a multi-line here-string or a long inline argument, in priority order:

- **Temp file (the reliable default).** Write the message to a file via the Write tool, then run a short `git commit -F path/to/file` via either shell, then delete the file. The long string never crosses any shell command line, so no encoded-command path can be triggered. Worked first try on this project.
- **Short inline `-m`.** Keep commit messages to a single line short enough that PowerShell can pass them as a normal arg. Reasonable for tight bug fixes; not for substantial features.
- **Bash heredoc — does NOT work for git on Windows.** `git commit -F /dev/stdin <<'EOF' ... EOF` fails with `fatal: could not read log file '/proc/self/fd/0'` because Git for Windows is a Windows-native binary that can't open POSIX paths. Don't reach for this; jump straight to the temp-file approach. (For non-git commands that read stdin natively, Bash heredoc may still work — but git is the common case where this comes up, and it doesn't.)

**Specifically NOT:**

- `git commit -m @'multi-line here-string'@` via PowerShell — this is the exact pattern that triggered the flag.
- Long multi-line PR body text inline to `gh pr create` via PowerShell — same shape, same problem. Use `--body-file <path>` instead.
- Long inline scripts via `powershell -Command "..."` with embedded newlines.

Temp-file is the durable answer on this project even though the standing convention is PowerShell-first per `feedback_use_powershell.md`. The two rules complement: PowerShell for everyday short operations, temp-file commit-message workflow for anything multi-line. The temp file lives one command (Write tool); the next command uses it; the third deletes it. No long string ever crosses a process boundary in serialised form.
