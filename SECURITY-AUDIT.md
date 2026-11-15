# Security

This page is short on purpose. It says how security is looked after in this repository, and it does not change when the code does.

## What runs automatically

- **Secret scanning.** Gitleaks scans every pull request, every push to `main`, and the full history once a week (`.github/workflows/security-scan.yml`).
- **Code scanning.** CodeQL analyses every pull request.
- **Dependencies.** Dependabot opens update pull requests for NuGet packages and GitHub Actions.
- **Review.** Every change reaches `main` through a pull request with an independent review and the required checks.

## Periodic review

The code, the workflows and the infrastructure definitions are reviewed for security on a regular schedule. Findings are tracked privately until they are fixed, so this repository does not carry a list of open concerns, and fixes arrive as ordinary pull requests.

The project's own audit rules are in [`audit-rules/security.md`](audit-rules/security.md).

## Reporting a problem

If you find a security problem here, please report it privately through GitHub's "Report a vulnerability" button on the repository's Security tab rather than in a public issue.

## Why there is no detailed posture document

Until November 2026 this file was a section-by-section record of the project's security posture. It was retired because a detailed record in a public repository is out of date within hours of each change and has to be re-read for what it gives away every time it is touched. Code comments and older posts that cite a section number of this file (for example "§11") refer to that earlier version, which remains in the git history.
