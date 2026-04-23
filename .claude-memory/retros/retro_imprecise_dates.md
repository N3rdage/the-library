---
name: Retro — imprecise dates
description: Most pre-modern books only carry month/year or just year — DateOnly was forcing a precision the data didn't have
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — new `DatePrecision { Day, Month, Year }` enum + companion column on both `Edition.DatePrinted` and `Work.FirstPublishedDate`. DateOnly storage stays the same (sortable; month/day default to 1) but display is precision-aware: "1973" / "Oct 1973" / "12 Oct 1973". Form inputs swapped to free-form text parsed by `PartialDateParser` accepting "1973", "1973-10", "10/1973", "Oct 1973", "12 October 1973". `BookLookupService` returns precision so Open Library year-only `publish_date` doesn't render as "1 Jan 1934".

**Surprise** — the design fork was easy in retrospect: keep DateOnly + add a precision flag, vs switch to free-text strings. Drew picked the precision flag without much hesitation but it was actually a load-bearing decision — preserving DateOnly meant existing sort/filter code Just Worked, where strings would have meant "let's parse on every query".

**Lesson** — when modelling "this thing has imprecise variants", look for the cheap composite (typed value + precision tag) before reaching for free-text. Sortability and queryability are way easier to lose than to add. Also: the migration is trivial (2 columns, default 0 = Day) — existing data lands as Day precision, which is wrong for some Open-Library-derived rows but fixable in-place by re-typing the date in the form.

**Quotable** — Drew: "Most books only give a month/year or just the year" — exact moment you know the abstraction needs a flexibility axis it didn't have. Common-case framing > rare-case framing.
