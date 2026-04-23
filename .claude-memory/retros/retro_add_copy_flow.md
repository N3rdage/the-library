---
name: Retro — add-copy detection + "Add another?" toggle
description: Re-scanning a book you already own should be a one-click "+1 copy", not a unique-key crash
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — Add Book's lookup checks the DB for the ISBN before doing the Open Library call. If found, surfaces a banner ("you already own this book — add another copy / edit existing") instead of the prefill. BulkAdd allows same-session re-scan; SaveBookAsync re-checks the DB at save time so the second scan becomes a `+1 Copy` instead of a unique-key crash. Edit Book's "Add edition" gains an ISBN lookup. Plus an "Add another?" checkbox on the Add page that resets the form in place after save (default off).

**Surprise** — the bug Drew described as "errors when I scan again" was three different bugs in three flows: (1) BulkAdd silently dedup'd in-session re-scans, (2) BookAdd had no duplicate check at all and crashed on `DbUpdateException`, (3) BulkAdd's existing duplicate path only ran if the row was flagged `IsDuplicate` at *scan* time, not at *save* time, so two same-session scans both went down the new-Book path. Different fix per layer.

**Lesson** — for "duplicate detection" features, the question to ask is "at what moment do we know the duplicate exists?". Scan time vs save time vs lookup time all matter, and they're sometimes different from each other. Ours: lookup-time check at the Add page (spare the user from typing if we know already), save-time re-check at BulkAdd (handles in-session races).

**Quotable** — the "Add another?" checkbox was almost lost as a bullet in Drew's request — I bundled it explicitly because we were already touching Add.razor. Small bundle that paid off because the flow was so much nicer to use during testing.
