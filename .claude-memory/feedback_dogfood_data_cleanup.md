---
name: dogfood-data-cleanup
description: "Prefer manual UI cleanup over scripts when judgement-per-row is involved, to dogfood the user experience"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 185d0f37-9e38-4cc9-ab78-8b6063f640f2
---

When data cleanup work could be done either via SQL script or via the Add/View/Edit UI, default to manual UI for anything that involves judgement-per-row (not pure bulk transforms).

**Why:** Drew called it out 2026-05-18 during the genre-restructure arc, deciding S3 (over-tagged Books spot-fixes) via UI: "normal users will not be able to just do data hotfixes, so I want to feel the pain (or lack thereof) in having to do things manually at times." Surfacing UX pain points through real use is one of his curation goals. Skipping straight to scripts robs him of that signal.

**How to apply:**
- **Script it** when the transform is uniform across rows, the rule is mechanical, AND there are enough rows to repay the script overhead — e.g. [[backfill-author-default-genres]] (S1: every Sapir Work → Thriller + Adventure, no per-row judgement), [[migrate-format-genres-to-tags]] (every Graphic Novels GenreWork → format:graphic-novel BookTag, deterministic), [[converge-destroyer-genres]] (35 Destroyer Works → Adventure-only).
- **Lean UI even for mechanical transforms when the volume is tiny** (~3–5 rows). 2026-05-22: I'd built `strip-christie-romance.ps1` for a pure bulk delete (Christie + Romance → gone); dry-run found 3 rows and Drew killed the script with *"if there are only 3 I will just do them manually"*. The dogfood-friction value at low volume beats the time saved scripting.
- **Lean UI** when each row needs a "is this the right call for *this* book?" decision — e.g. S3 over-tagged spot-fixes (different noise pattern per book), S2 unknown-author re-attribution (each book's true author differs).
- When unsure, **offer both options** with Drew picking — don't assume scripting is the right answer because it's faster.

**Rough threshold:** if the dry-run count comes back in single digits, propose manual UI even if the rule is mechanical. Above ~10 rows, the script earns its keep.

Related: [[user_drew]] (curator + solo-dev frame), [[feedback_planning_conventions]] (plan first, surface options).
