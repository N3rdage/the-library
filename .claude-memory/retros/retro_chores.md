---
name: Retro — chore arc (license, sub-genres, UI tweaks)
description: Several small follow-up PRs covering MIT license, Horror sub-genres seeding, and the blazor-error-ui CSS bug
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — three small chore PRs:
1. MIT license file at repo root.
2. Three Horror sub-genres seeded via idempotent EF migration (`Cthulhu Mythos`, `Vampire`, `Zombie`).
3. UI fixes: missing `#blazor-error-ui` CSS rule (causing "An unhandled error has occurred" to sit visible on every page) + BulkAdd OCR camera kept live across captures so Drew can batch-photograph the no-barcode books.

**Surprise** — the blazor-error-ui bug had been there since project init and Drew just lived with it. The default Blazor template ships the `display: none` rule in `wwwroot/css/site.css`; this project's site.css was missing it. Five lines of CSS that would have taken 30 seconds to fix at any point in the project's history.

**Lesson** — small chore PRs are cheap to ship and clean to review; don't bundle them into bigger features unless there's a real reason. The horror sub-genres + the genre matcher fix could have shared a PR but didn't, and history is much clearer for it. Also: when a user mentions "I see something weird at the bottom of every page", check the boring-CSS hypothesis before assuming a JS bug.

**Quotable** — three of these five-line PRs were merged in a single afternoon and felt better than one chunky feature commit. Validates the "small PR" rhythm — even the trivial ones earn their keep.
