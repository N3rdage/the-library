---
name: Do not push branches or open PRs — ask the user to do it
description: Workflow rule for GitHub interaction on this project — Claude stages/commits locally but hands off push + PR creation to the user, who also confirms merge.
type: feedback
originSessionId: 8924a252-f12e-4d8b-98b2-37192eef16f6
---
When work is ready to leave the local machine, do **not** run `git push` or try to create a pull request. Instead, tell the user: **"push the changes and create a PR"** and stop. Wait for the user to confirm the PR has been merged into `main` before treating the change as landed or moving on to follow-up branches.

**Why:** The user wants control over what hits the remote and when the PR is opened. Unilateral pushes bypass that review step.

**How to apply:**
- Local work (branch creation, commits, building, testing) is fine to do autonomously.
- After the final commit on a feature branch, do not push — output the hand-off phrase.
- Do not poll, retry, or assume merge status. Wait for the user to say it's merged.
- Only after the user confirms merge should you `git checkout main`, `git pull`, and start the next branch.
