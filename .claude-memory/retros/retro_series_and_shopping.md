---
name: Retro — Series + Shopping mode arc
description: 8-PR sequence (PRs #34-#41) — Series entity, gap detection, mobile-first shopping page
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit messages of PRs #34 through #41 (2026-04-17).

**Shipped** — eight PRs ran end-to-end in one day (#34 through #41):
- Series entity (Series vs Collection, ExpectedCount for numbered series)
- Series list / edit pages
- Series assignment from book edit
- Auto-detection on Add and Bulk Add (suggest series when author has existing books)
- WishlistItem extensions (ISBN + Series fields)
- Shopping mode page with ISBN scan + text search ("do I have this?")
- Series gaps section ("you have books 1, 3, 5 of this 7-book series — missing 2, 4, 6, 7")
- Shopping list with quick-add and "bought → add to library" action

**Surprise (inferred)** — the Shopping page is a distinct *mode* not a feature. Mobile-first ("am I standing in a bookshop?"), camera scanner first, optimised for one-handed use. The page wraps three workflows (do-I-have-it, series gaps, wishlist) under one route because they all answer the same higher-order question: "what should I be looking for right now?". Branding the route as a *mode* rather than splitting into three pages probably explains why it stayed coherent.

**Lesson** — recurring "do I have this?" lookups need at least three input modes (barcode scan, text search, manual ISBN) because users aren't always in barcode-scanning conditions. The bookshop has good lighting, the bedside table at 11pm doesn't. Multi-input UX from the start, not retrofitted.

**Quotable** — eight PRs in a single day with no fix-up commits in the days following. Mobile-priority memory rule (`feedback_mobile_priority.md`) presumably came out of this arc — the rule itself is broad enough to apply to any future feature.
