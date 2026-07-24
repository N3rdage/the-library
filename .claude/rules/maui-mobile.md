---
paths:
  - "BookTracker.Mobile/**"
  - "BookTracker.Mobile.Cache/**"
---

# MAUI / Mobile.Cache gotchas (Bookshelf)

Auto-loaded when touching `BookTracker.Mobile/**` or `BookTracker.Mobile.Cache/**`. Sections keep their original memory names so `[[feedback_*]]` cross-references still resolve by grep.

## feedback_maui_native_control_sizing


A .NET MAUI control backed by a native Android surface — e.g. ZXing's `CameraBarcodeReaderView`, map views, web views, media players — does **not** reliably honour its own `HeightRequest`. The native preview surface sizes to fill and can z-order *over* sibling/parent content. Setting `HeightRequest` on the control, or on a wrapper around it, can do nothing.

**Why:** the inline barcode scanner on Bookshelf's FindPage (2026-06-16) filled the entire tab — the page header + search bar were drawn over — despite sitting in a wrapper with `HeightRequest="220"` in an `Auto` grid row. The native surface obeys the *layout slot it is handed*, not a requested size.

**How to apply:** bound it with the layout, not a request. Put the control in a parent whose rectangle is fixed — a `Grid` row with an explicit pixel `Height` (name the `RowDefinition` and toggle its `Height` 0↔N in code to collapse/expand, rather than relying on the child's `IsVisible` + `HeightRequest`), plus `IsClippedToBounds="True"` on the container. The explicit row rect is what the native surface fills and clips to. Verify on-device — emulators and the layout previewer don't always reproduce native-surface behaviour. Generalises to any native-backed view that won't stay in its box. See [[retro_bookshelf_redesign_arc]].

## feedback_sqlite_net_pcl_schema_backfill


When adding a new column to a `Cached*` entity in `BookTracker.Mobile.Cache/Models/CacheEntities.cs` that participates in any query (indexed search, filter, sort), add a one-shot `UPDATE` in `CatalogCache.InitAsync` after the `CreateTableAsync` calls to backfill existing rows.

**Why:** sqlite-net-pcl's `CreateTableAsync` handles schema evolution by issuing `ALTER TABLE ADD COLUMN` for any properties missing from the existing table, but doesn't populate the new column on existing rows — they get NULL. Tests always run on fresh DBs (GUID temp paths in `NewCacheAsync`) so the new feature works in CI, but real devices that updated from a previous build have NULL for the new column on every existing row. The failure mode is silent: the query just returns nothing for the bulk of the catalogue. First hit 2026-05-14 with `TitleLower` for the title-search feature — search returned zero results against Drew's 1,146-book catalogue until the backfill landed.

**How to apply:** In `CatalogCache.InitAsync`, after the block of `CreateTableAsync` calls, before the `_coversDir` setup, append a `_db.ExecuteAsync("UPDATE {table} SET {newcol} = {expr} WHERE {newcol} IS NULL OR {newcol} = ''")` for each new queryable column. Idempotent — no-op once rows are populated. Microseconds even at the 3000+ books target. Stack them as a numbered list of backfills if multiple bumps accrue. Tested via the pattern in `InitAsync_BackfillsTitleLowerForLegacyRowsWrittenBeforeTheColumnExisted` — populate, sabotage the column via a parallel SQLite connection, re-open via InitAsync, assert the query works. Related: [[runbook_adb_service_after_reboot]] for diagnosing Bookshelf bugs that look like build/install failures but are really data-state issues.
