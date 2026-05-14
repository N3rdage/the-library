---
name: sqlite-net-pcl-schema-backfill
description: "When adding a queryable column to a Bookshelf Cached* entity, backfill existing rows in CatalogCache.InitAsync — CreateTableAsync ALTERs but doesn't populate, and the failure mode is silent (\"search returns nothing\")."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 357f34b8-2b9f-4268-b445-cf71fd75fda6
---

When adding a new column to a `Cached*` entity in `BookTracker.Mobile.Cache/Models/CacheEntities.cs` that participates in any query (indexed search, filter, sort), add a one-shot `UPDATE` in `CatalogCache.InitAsync` after the `CreateTableAsync` calls to backfill existing rows.

**Why:** sqlite-net-pcl's `CreateTableAsync` handles schema evolution by issuing `ALTER TABLE ADD COLUMN` for any properties missing from the existing table, but doesn't populate the new column on existing rows — they get NULL. Tests always run on fresh DBs (GUID temp paths in `NewCacheAsync`) so the new feature works in CI, but real devices that updated from a previous build have NULL for the new column on every existing row. The failure mode is silent: the query just returns nothing for the bulk of the catalogue. First hit 2026-05-14 with `TitleLower` for the title-search feature — search returned zero results against Drew's 1,146-book catalogue until the backfill landed.

**How to apply:** In `CatalogCache.InitAsync`, after the block of `CreateTableAsync` calls, before the `_coversDir` setup, append a `_db.ExecuteAsync("UPDATE {table} SET {newcol} = {expr} WHERE {newcol} IS NULL OR {newcol} = ''")` for each new queryable column. Idempotent — no-op once rows are populated. Microseconds even at the 3000+ books target. Stack them as a numbered list of backfills if multiple bumps accrue. Tested via the pattern in `InitAsync_BackfillsTitleLowerForLegacyRowsWrittenBeforeTheColumnExisted` — populate, sabotage the column via a parallel SQLite connection, re-open via InitAsync, assert the query works. Related: [[runbook_adb_service_after_reboot]] for diagnosing Bookshelf bugs that look like build/install failures but are really data-state issues.
