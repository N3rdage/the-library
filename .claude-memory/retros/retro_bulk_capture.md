---
name: Retro — bulk capture (ISBN entry → barcode scan → photo OCR)
description: Three-stage evolution of the rapid book-entry workflow (PRs #17, #18, #52)
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit messages of PRs #17, #18, #52 (2026-04-15 to 2026-04-18).

**Shipped** — three layered captures of the same problem: "I have a stack of books and I want them in the library quickly":
1. **PR #17** — `/books/bulk-add` page with type-or-paste ISBN + async lookup (Open Library → Google Books) into a discovery grid. Per-row Accept / Follow-up / Duplicate handling. "Accept all found" batch button.
2. **PR #18** — `html5-qrcode` integration: phone camera reads EAN-13 barcodes and feeds straight into the same grid. Rear-facing mode by default. Auto-disposes on navigation.
3. **PR #52** — photo ISBN OCR via Claude Sonnet vision. For older books with no barcode, the user photographs the printed ISBN, image is base64-JPEG'd to Sonnet with a constrained "extract only the digits" prompt, result feeds the grid.

**Surprise (inferred)** — each layer reused the discovery-grid plumbing without restructure. The first PR's `DiscoveryRow` data shape and "Accept / Follow-up" actions stayed exactly the same; PRs #18 and #52 just added new ways to *populate* the row. Clean separation between input modality and capture logic paid off three times.

**Lesson** — for rapid-iteration UX, get the *back-end of the workflow* (discovery grid + actions) right first, then bolt on input modes. Doing it in the other order would have meant rebuilding the grid each time. Also: photo OCR via vision model is way more accurate than expected — Drew now uses it for his 1960s-era hardcovers without much friction.

**Quotable** — the photo OCR feature went from "wouldn't it be cool if…" to shipped in a single PR (#52). The willingness to throw an LLM at small ad-hoc problems unlocks features that would otherwise cost weeks of OCR engineering.
