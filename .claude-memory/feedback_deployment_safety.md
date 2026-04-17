---
name: Deployment and migration safety
description: All migrations must retain existing data. Breaking model changes need defaults and review tags.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
Always discuss deployment impact in feature plans. Once the system is in use, data loss is unacceptable.

All EF migrations must retain existing data. If a model change is breaking (e.g. new required column on existing table), add a default value and consider adding a "review" tag so the user can audit affected records.

**Why:** The system will have real user data. A migration that drops or corrupts data is worse than a delayed feature.

**How to apply:** In every plan that involves model changes, explicitly state the migration strategy and confirm data is preserved. Flag any migration that could be destructive.
