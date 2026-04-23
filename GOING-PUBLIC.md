# Going public — decision doc

Planning for TODO #3. Answers: should the repo be public? If so, as one repo or split? What needs doing before the flip, and what gets unblocked after?

## TL;DR

**Recommendation: flip this repo to public as a single repo, no split.** Nothing credential-shaped is committed; the security audit confirmed it and the stranger's-eyes pass below re-confirms it. The one decision point worth a deliberate call is **how much of `.claude-memory/` should stay tracked once public** — no files contain secrets, but `user_drew.md` in particular documents collaboration style in a way some developers prefer kept private. Options below.

No history rewrite is required for the flip itself. If any `.claude-memory/` file moves to local-only, its historical content remains visible via `git log` forever unless scrubbed with `git-filter-repo`; acceptable for workflow-preference content but worth calling out.

## What's already clean

Confirmed via the `2026-04-23` security audit (`SECURITY-AUDIT.md`) and re-walked with a "what does a stranger see?" lens:

| Area | What's visible | Risk |
|---|---|---|
| `infra/*.bicep` | Resource-group naming, KV structure, RBAC role IDs (Azure public GUIDs), Private Endpoint topology, AI provider wiring | None. Architecture is portfolio-positive; no credentials. |
| `infra/*.ps1` | Deploy / OIDC-setup / secret-rotation / refresh-local-db scripts | None. All secrets pass through params + KV. The dev-only docker SA password `BookTracker!Dev1` is documented as such. |
| `.github/workflows/*.yml` | CI + deploy + swap + rotation + security-scan pipelines | None. Secrets referenced by variable name only; OIDC federation, no long-lived secrets. |
| `appsettings.*.Example.json` | Config templates | None. Empty strings / `<placeholder>` values. |
| Custom domain `books.silly.ninja` | Visible in infra docs | None. Already resolves publicly via DNS. |
| Azure subscription / tenant / resource names | In workflow variable *names*, not values | None. Values live in GitHub Actions Variables (not committed). |
| Full git history secret scan | 228 commits | Clean per gitleaks + manual pattern sweep. |

## The one decision: `.claude-memory/` exposure

Currently tracked in git (14 files):
- `MEMORY.md` — index of memory files
- 11× `feedback_*.md` — workflow preferences (e.g. "don't push from Claude", "always update ARCHITECTURE.md on structural changes")
- `project_performance_target.md` — one-line performance target (3000+ copies)
- `user_drew.md` — user profile describing collaboration style

Currently **untracked** (local-only, session-generated):
- `retros/` — 30+ per-feature retrospectives
- `patterns.md` — working patterns extracted across the arc
- `project_blog.md` — blog project brief
- A handful of newer `feedback_*.md` files not yet committed

Making the repo public would expose only the 14 tracked files. Untracked content stays local regardless.

**Three options for the tracked `.claude-memory/` content:**

### Option A — Keep all 14 tracked (my lean for most personal projects)

Positives: transparency about workflow + AI collaboration discipline is interesting portfolio content. Several of the feedback files (PR-breakdown conventions, deployment safety, testing conventions) demonstrate thoughtful dev practice. Zero security implication.

Negatives: `user_drew.md` is the most personal file (describes collaboration style). Some developers would rather not have "Drew is terse, trusts defaults, prefers single PRs" indexed by search engines and AI crawlers alongside their name.

### Option B — Untrack `user_drew.md` only; keep feedback + MEMORY

Compromise. Workflow preferences stay public (they're professional + portfolio-positive); the one file that reads more as personal profile goes local-only.

Steps: `git rm --cached .claude-memory/user_drew.md`, add `.claude-memory/user_drew.md` to `.gitignore`, commit. The file survives locally; git history still contains its prior content unless separately scrubbed.

### Option C — Untrack all of `.claude-memory/` before flipping

Most conservative. Put `.claude-memory/` in `.gitignore`, `git rm --cached -r .claude-memory/`, commit. All 14 files leave the working tree's tracked set; they persist locally; git history still contains the content.

Downside: loses the transparency + "here's how this was built with Claude" portfolio angle. For a project whose whole build process includes the memory system as a feature, this option somewhat undersells what's interesting about it.

### Recommendation

**Option B.** Keep the professional workflow-preference files visible (they're a feature, not a liability); untrack the user profile. If you later decide you want `user_drew.md`'s historical content scrubbed from git too, a single `git-filter-repo --invert-paths --path .claude-memory/user_drew.md` rewrite would do it — same procedure as the recent email rewrite.

**For untracked content** (`retros/`, `patterns.md`, `project_blog.md`): these are the most portfolio-valuable content, and all currently local-only. After the flip, separate decision per-file whether to start tracking them. The retros in particular are interesting external-facing content — consider promoting some to a public blog (which is already TODO #17).

## Why single repo, not split

Briefly considered:

- **Public app + private infra** — split into two repos, `booktracker` (code) and `booktracker-infra` (Bicep/PowerShell/workflows). Pros: `infra/` stays hidden. Cons: every infra-touching PR is two coordinated PRs; secrets and variables pipe between repos awkwardly; deploy workflow needs significant rework since it currently references infra-relative paths.
- **Public fork of private** — reverse: keep private, maintain a scrubbed public mirror. Highest maintenance cost. Only justified for proprietary secrets in infra — not our case.

Neither delivers value over a single public repo for this project. The audit confirmed `infra/` contains no secrets; exposing architecture patterns is a portfolio positive, not a cost.

## Pre-flip checklist

Before clicking "Change visibility → Public" in GitHub Settings:

- [ ] Decide `.claude-memory/` strategy (A / B / C above). If B or C, land the commit + `.gitignore` update first.
- [ ] Re-run gitleaks one more time as a sanity net: `gitleaks git --no-banner .`
- [ ] Skim the last 5 PR diffs for anything committed since the security audit (`git log --since="2026-04-23"` + eyeball each).
- [ ] Decide: leave `TODO.md` public or gitignore it? My lean: leave public — it's interesting project signal, and blocked/deferred items are normal in any real project.
- [ ] Decide: leave `SECURITY-AUDIT.md` public? My strong lean: yes — it's one of the higher-value portfolio artefacts in the repo.
- [ ] Decide: add a `LICENSE` file? Currently MIT-licensed per an early commit, but confirm before flipping. Public visibility without a LICENSE is ambiguous.
- [ ] Decide: update `README.md` landing content? Repos going public usually benefit from a less "notes to self"-style README and more "here's what this is and why you might care." Current state: thin. A ~40-line README upgrade is a small pre-flip PR.

## Post-flip actions (unblocked by the flip)

Once public, these become actionable:

- **TODO #2 — GitHub Environment w/ required reviewers for slot swap.** Environments become available. Decide: reviewer-approval (still needs a second human), wait-timer (cancellable delay, works solo), or deployment-branches restriction (forces swaps from main). My lean on solo: wait-timer of 2-5 minutes — cheapest cancellation-window guard without a collaborator.
- **Dependabot visibility + alerts** upgrades for public repos (security advisory scoring, broader CVE coverage).
- **GitHub Actions minutes** — public repos get unlimited minutes on standard runners; currently on the private-repo 2000/month cap (not close to it but nice).
- **Community health** — GitHub's "community standards" checklist (CONTRIBUTING, CODE_OF_CONDUCT, ISSUE_TEMPLATE, PR_TEMPLATE) appears. Drew can decide which to adopt; for a personal-use portfolio project, CONTRIBUTING is already present and the rest is optional.
- **Blog the build** — the whole AI-collaborated development arc is a genuinely novel portfolio story. `.claude-memory/retros/` has the raw material (TODO #17 already tracks this).

## Consequences / things that don't change

- **GitHub usernames + commit emails** — already `N3rdage <N3rdage@users.noreply.github.com>` after the history rewrite. No further cleanup needed.
- **Azure subscription** — unchanged. A stranger seeing `infra/` learns the subscription *exists* (subscription ID isn't in repo) and sees the resource-group naming convention. Not a vulnerability.
- **Attacker awareness** — going public slightly raises the "this app exists" signal. The defense-in-depth (Easy Auth, KV private endpoint, SQL private endpoint, VNet-peered OpenAI, managed identities) is what actually protects against attack, not the obscurity of the codebase.

## What I'd do next, concretely

1. **Pick the `.claude-memory/` option.** My recommendation: B.
2. **Small pre-flip hygiene PR**: land the B-flavour untrack-user_drew + one README polish pass (make the first-time-visitor experience decent). ~2 files, one PR.
3. **Flip visibility** in GitHub Settings.
4. **Open new TODO row**: "Set up GitHub Environment for swap.yml with wait-timer protection" (now unblocked). Small S.
5. **Close TODO #3** (this row) with a note pointing at the new row.

Anything I should rethink?
