---
name: Performance target — 3000 book copies
description: All screens showing multiple books must handle 3000+ copies without performance issues.
type: project
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
The system should be able to handle 3,000 book instances (copies). All screens that display multiple books (library list, search results, bulk add grid, home dashboard stats) must consider this as part of layout design and query performance.

**Why:** This is the expected scale of Drew's library. Designs that paginate, virtualise, or limit query results are preferred over loading everything at once.

**How to apply:** When planning or reviewing any feature that lists books, flag any design that might struggle at 3000+ copies (e.g. loading all books without pagination, client-side filtering of full dataset, unbounded genre/tag queries). The library list already paginates at 20 — this is the right pattern.
