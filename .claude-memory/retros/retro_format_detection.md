---
name: Retro — format detection + backfill marker pattern
description: Richer BookFormat enum, Open Library physical_format normalizer, and the MaintenanceLog marker that got reused
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — `BookFormat` expanded from `Hardcopy/Softcopy` to `Hardcover/TradePaperback/MassMarketPaperback/LargePrint`. ISBN lookup now reads Open Library's `physical_format` and `physical_dimensions` and infers a format. Existing books got reclassified by a one-shot startup `BackgroundService` (`EditionFormatBackfillService`) gated by a `MaintenanceLog` marker.

**Surprise** — the real win wasn't the enum or the parser, it was the `MaintenanceLog` table. We needed a way to say "this one-shot data fix has run, don't run it again". A new generic table (`Name unique`, `CompletedAt`, `Notes`) ended up reused for the genre backfill in the very next feature. Scratched a recurring itch we didn't know we had.

**Lesson** — preserved-ordinal enum migration is *very* nice when it works (`Hardcopy=0` stayed at value 0 as `Hardcover=0`, no SQL update needed). For backfills, pulling the marker check + throttle + delay into a dedicated `BackgroundService` per job kept the code obvious; resisting the urge to build a generic "data migration runner" abstraction was right — two instances isn't enough to abstract.

**Quotable** — when Drew came back saying Christie books were getting tagged "Science Fiction" and "Romance", the format-detection backfill code was sitting right next to the genre-matching code that turned out to be the actual culprit.
