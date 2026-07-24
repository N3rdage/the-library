#Requires -Version 7
<#
.SYNOPSIS
    Wire Claude Code's auto-memory into this repo's committed .claude-memory\ directory.

.DESCRIPTION
    Claude Code keeps auto-memory in a machine-local, per-project directory:
        ~\.claude\projects\<repo-slug>\memory\
    where <repo-slug> is the repo's absolute path with every non-alphanumeric
    character replaced by '-' (e.g. C:\Users\me\code\The Library ->
    C--Users-me-code-The-Library).

    This project instead keeps its durable memory *in the repo* at .claude-memory\
    (git-tracked) so retros/feedback/runbooks travel with the code. This script
    symlinks the machine-local auto-memory directory to the in-repo one, so
    auto-memory loads the committed MEMORY.md index and new notes land in git.

    Run once per machine. Idempotent: re-running when the link already points at
    the right place is a no-op. It refuses to clobber a real (non-symlink)
    directory that already holds files.

    NOTE: creating a symlink on Windows needs Developer Mode enabled (Settings ->
    Privacy & security -> For developers) or an elevated shell.

.EXAMPLE
    pwsh -File scripts\link-claude-memory.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Repo root (this script lives in scripts\, so its parent is the root).
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$memoryDir = Join-Path $repoRoot '.claude-memory'

if (-not (Test-Path -LiteralPath $memoryDir)) {
    throw "Expected committed memory directory not found: $memoryDir"
}

# Derive the slug exactly as Claude Code does: every non-alphanumeric char -> '-'.
$slug = ($repoRoot -replace '[^A-Za-z0-9]', '-')
$linkPath = Join-Path $env:USERPROFILE ".claude\projects\$slug\memory"
$projectDir = Split-Path -Parent $linkPath

Write-Host "Repo memory : $memoryDir"
Write-Host "Auto-memory : $linkPath"

# Already linked correctly?
$existing = Get-Item -LiteralPath $linkPath -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.LinkType -eq 'SymbolicLink') {
        # .Target is the path the symlink points at (not the link's own path).
        $memoryResolved = (Resolve-Path -LiteralPath $memoryDir).Path
        $targetResolved = $null
        if ($existing.Target) {
            $r = Resolve-Path -LiteralPath $existing.Target -ErrorAction SilentlyContinue
            if ($r) { $targetResolved = $r.Path }
        }
        if ($targetResolved -eq $memoryResolved) {
            Write-Host 'Already linked. Nothing to do.' -ForegroundColor Green
            return
        }
        throw "A symlink already exists but points elsewhere ($($existing.Target)). Remove it and re-run."
    }

    # A real directory. Only safe to replace if it's empty (auto-memory placeholder).
    $contents = Get-ChildItem -LiteralPath $linkPath -Force
    if ($contents) {
        throw "A real (non-symlink) directory with files already exists at $linkPath. " +
              'Back up / merge its contents into .claude-memory\ manually, remove it, then re-run.'
    }
    Remove-Item -LiteralPath $linkPath -Force
}

# Ensure the parent projects\<slug>\ directory exists, then create the link.
if (-not (Test-Path -LiteralPath $projectDir)) {
    New-Item -ItemType Directory -Path $projectDir -Force | Out-Null
}

New-Item -ItemType SymbolicLink -Path $linkPath -Target $memoryDir | Out-Null
Write-Host 'Linked. Auto-memory now reads/writes the repo .claude-memory\ directory.' -ForegroundColor Green
