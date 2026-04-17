---
name: Feature planning conventions
description: Rules for how to structure feature plans, PR sizing, complexity callouts, and when to plan.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
Always plan before implementing, even for small changes. If Drew doesn't use the "plan:" prefix, plan anyway. For truly trivial items (typo, single-line fix), gently note it may not have needed the plan phase, but still plan it.

**PR breakdown:** Always suggest a PR breakdown for medium+ complexity features. Small changes can be a single PR.

**Single vs multiple PRs:** Prefer one concern per PR. Multiple concerns in a single PR are OK when: (a) separating them would be more complex (ask for clarification if unsure), or (b) the total impact is small (3 files or fewer, or a few single-line changes).

**Complexity callout:** If a change is complex or touches 5+ files, explicitly flag it as a complex task in the plan.

**Plan structure:**
- What changes, broken into logical sections
- New/modified files
- Open questions with suggested defaults
- PR breakdown (for medium+ features)
- Mobile priority question
- Performance considerations (3000+ copies)
- Deployment impact (migrations, data safety)

**Why:** Drew wants to test planning everything to build good habits. Planning catches issues early and helps align on scope before code is written.

**How to apply:** Every feature request gets a plan proposal first. Flag complexity, suggest PR splits for medium+ work, and include all standard questions.
