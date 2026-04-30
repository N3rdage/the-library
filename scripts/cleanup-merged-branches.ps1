# Deletes local branches whose upstream remote has been deleted — typically
# the squash-merge orphans GitHub leaves behind after merging + auto-deleting
# a PR's branch.
#
# Squash-merge flattens a feature branch's commits into one commit on main, so
# git can't see the squash-merge content as merged via ancestry. `git branch -d`
# refuses, the local branch lingers, and `git branch` output gradually fills
# with cruft.
#
# This script:
#   1. Runs `git fetch --prune` to drop remote-tracking refs for branches
#      GitHub has deleted.
#   2. Lists local branches whose upstream is now `[gone]`.
#   3. By default, prints what it would delete (dry run).
#   4. With -Force, runs `git branch -D` on each. Skips main / master.
#
# Run from the repo root:
#   pwsh ./scripts/cleanup-merged-branches.ps1            # dry run
#   pwsh ./scripts/cleanup-merged-branches.ps1 -Force     # actually delete

[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

Write-Host "Pruning remote-tracking refs..."
git fetch --prune | Out-Null

# `git branch -vv` lines look like:
#   "  feat/old-thing   abc1234 [origin/feat/old-thing: gone] msg"
#   "* current-branch   def5678 [origin/current-branch] msg"
# We want only the ones with `: gone]` markers, then extract the branch name.
$gone = git branch -vv |
    Where-Object { $_ -match ': gone\]' } |
    ForEach-Object {
        # Strip the leading "* " (current-branch indicator) or "  " then take
        # the first whitespace-delimited token, which is the branch name.
        $line = $_ -replace '^\s*\*?\s*', ''
        ($line -split '\s+', 2)[0]
    }

if (-not $gone) {
    Write-Host "No stale local branches to delete."
    exit 0
}

Write-Host ""
Write-Host "Local branches with deleted remotes:"
$gone | ForEach-Object { Write-Host "  - $_" }
Write-Host ""

if (-not $Force) {
    Write-Host "(dry run — pass -Force to actually delete)"
    exit 0
}

foreach ($b in $gone) {
    if ($b -in 'main', 'master') {
        Write-Warning "Skipping protected branch: $b"
        continue
    }
    Write-Host "Deleting $b..."
    git branch -D $b
}
