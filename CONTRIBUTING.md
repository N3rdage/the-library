# Contributing

## Branching

`main` is protected and always deployable. **No direct commits or pushes to `main`** — every change lands via a pull request from a feature branch.

### Branch naming

- `feat/<short-name>` — new features (e.g. `feat/wishlist-import`)
- `fix/<short-name>` — bug fixes (e.g. `fix/rating-range-validation`)
- `chore/<short-name>` — tooling, build, dependency bumps
- `docs/<short-name>` — documentation-only changes
- `refactor/<short-name>` — non-behavioral code changes

### Sub-branches

Large features can be staged. Branch off the parent feature branch, keep the same prefix, and add a second segment:

```
feat/wishlist-import              (parent feature)
feat/wishlist-import/csv-parser   (sub-branch, PR targets the parent)
feat/wishlist-import/ui           (sub-branch, PR targets the parent)
```

Merge sub-branch PRs into the parent feature branch; merge the parent into `main` when the whole feature is ready.

### Workflow

```powershell
git checkout main
git pull
git checkout -b feat/<short-name>
# ...commit work...
git push -u origin feat/<short-name>
# open a PR on GitHub targeting main
```

Delete the branch after the PR is merged.

## Pull requests

- Keep PRs focused — one concern per PR.
- Squash-merge into `main` to keep history linear.
- A PR cannot be merged without an approving review and passing required status checks (enforced by branch protection).
