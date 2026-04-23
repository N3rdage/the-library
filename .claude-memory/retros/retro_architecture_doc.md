---
name: Retro — adding ARCHITECTURE.md (the discipline of writing it down)
description: PR #50 — first time the project had a single living document of how it fit together. The discipline of keeping it updated became its own value.
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit message of PR #50 (2026-04-17).

**Shipped** — `ARCHITECTURE.md` at repo root. First version covered: solution structure, data model (Book → Edition → Copy hierarchy, Series, Genres, Tags), MVVM pattern with ViewModel conventions and lifetimes, DbContext factory pattern for Blazor Server, all pages/routes with purpose descriptions, shared components and services, barcode scanning setup, mobile responsiveness approach, testing strategy, configuration, infrastructure, and key conventions. Closing line: "This document should be updated when structural changes are made."

**Surprise (inferred)** — adding the doc was the cheap part; *committing to keep it updated* was the load-bearing decision. The project later got a memory rule (`feedback_architecture_doc.md`) that codified the convention so it survived across Claude sessions. Subsequent feature PRs in this session (Work refactor, pen names, library groupings) all touched ARCHITECTURE.md as part of the same change — not as a separate doc PR — which is what kept it from rotting.

**Lesson** — a living architecture doc is only worth writing if you have a mechanism to keep it alive. For us that mechanism was: (a) the memory rule reminding Claude to update it, (b) bundling doc updates into the feature PR not a follow-up. Neither would have worked alone — a memory rule with no co-located commits would drift, and co-located commits with no rule would get forgotten when the feature was big enough to be tiring.

**Quotable** — most of the time when you read ARCHITECTURE.md you're not looking for new information, you're confirming you remember the system correctly. That's the value: trustable confirmation. Its decay rate is what determines whether it's worth opening at all.
