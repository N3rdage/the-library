---
name: Bookshelf UI redesign arc — Shell + tabs, unified Find, inline scanner, motion
description: 2026-06-15 → 2026-06-16. Implemented docs/BOOKSHELF-UI-REDESIGN.md as a multi-PR arc that replaced the MAUI app's hub-and-spoke MainPage with a Shell + 3 bottom tabs (Find / Wishlist / Gaps). Shipped: foundation tokens (#337), Shell+tabs (#338), status sheet + SyncService + auto-sync (#339), AuthorWorksPage (#340), unified FindPage + ResultPage (#341), inline ZXing scanner + ScanPage deletion (#342), arc-review fixes (#343), Wishlist sort/grouping + Gaps progress/pills, motion layer (§9). Closed TODO #37 (palette + dark coverage); spawned #54 (online "do better when online" layer — cache holds only owned books so v1 is offline search/scan of what you own). Durable lessons: offline-first scope discipline (name what the cache can't answer and arc it separately); defer the high-effort review to ARC END for multi-PR arcs (stops "we do that next PR" false negatives); ZXing's native Android camera surface ignores a parent's HeightRequest (bind it with an explicit Grid row); and verify warnings with --no-incremental (incremental masks them) → TD-11 warnings-as-errors. F7/F8/F10 banked as TD-12/13/14.
type: project
originSessionId: 90df9d68-a582-4a86-895a-e1efab27cd96
---
## Shipped

~7 code PRs + chores over two days, off a written brief (`docs/BOOKSHELF-UI-REDESIGN.md`) produced by a separate design agent. The IA decision that drove everything: **bottom tabs over hub-and-spoke**, because the in-shop loop (scan → check author → check gaps → check wishlist, all for the book in hand) is co-equal tasks rapidly interleaved, not a menu.

- **#337 foundation** — leather/brass/parchment token set with light/dark pairs in `Colors.xaml`; semantic styles; `ThemeColors` bridge (`SetThemeColor` → `SetAppThemeColor`) so imperatively-built pages get live theme switching.
- **#338 Shell + tabs** — `AppShell` TabBar (Find/Wishlist/Gaps). DI gotcha: ShellContent `ContentTemplate` instantiates via parameterless ctor → fails for DI pages; fixed by injecting the tab pages into `AppShell`'s ctor and setting `ShellContent.Content` directly.
- **#339 status sheet + `SyncService` + auto-sync** — lifted auth/online/sync state out of the old MainPage into a singleton `ISyncService` (shared by the sync chip, the status sheet, and auto-sync-on-launch). Removed the pre-counted "pending updates" prompt.
- **#340 `AuthorWorksPage`** — owned works by author (replaces AuthorBooksPage). v1 owned-only; "Missing" is online (#54).
- **#341 `FindPage` + `ResultPage`** — unified debounced search (authors + works) + All/Authors/Works scope segment + ISBN auto-detect → `ResultPage` ("do I own this?").
- **#342 inline scanner** — embedded `CameraBarcodeReaderView` in FindPage, deleted `ScanPage` (Drew: full-screen camera for a barcode "looks super ugly"). Closed F3 (last light-only page gone).
- **#343 arc-review fixes** — F1 (scanner double-push interlock) + F5 (shared `CleanIsbn`); then F2 (Bought try/catch), F3 (sync concurrency gate), F4 (StatusSheet OnAppearing guard), F6 (marshal `StateChanged`), F9 hygiene (dead CTS/glyphs/tokens).
- **Wishlist/Gaps polish** — Wishlist Priority/Author/Series sort + High/Medium/Low group headers; Gaps progress bar + missing-volume pills (run-collapse `#6–#8`).
- **Motion layer (§9)** — `Motion.InAsync`/`PulseAsync` + Android animator-scale reduced-motion guard; tab cross-fades, result reveal, scan haptic+reticle pulse, Bought collapse.

Scope discipline up front: the "do better when online" features (work-level ownership, author Missing-bibliography, other-editions) were named and **arced out to #54** before any Find code — because the offline cache holds *only owned books*, so v1 Find can only answer "do you own this?", never "what are you missing?".

## Surprise

- **The inline camera filled the whole tab.** ZXing's `CameraBarcodeReaderView` renders a native Android preview surface that **ignores a parent container's `HeightRequest`** and expands to fill (and z-orders over the header + search bar). My first fix (Auto row + wrapper `HeightRequest=220`) did nothing. The surface honours the *layout rectangle*, not a requested size — so the fix was an explicit fixed-height Grid row (`CameraRow`, toggled 0↔220 in code) + `IsClippedToBounds`. Then two cosmetic follow-ups Drew caught on-device: a fixed-width button clipped "Cancel"→"Canc" (let it auto-size), and the `#B3000000` letterbox dimming read as a flat grey box at the small inline size (dropped it for a bare green reticle over the live preview).
- **"0 warnings" was a lie told by incremental builds.** I reported the motion PR clean off `dotnet build`. Drew ran a full rebuild and surfaced 5 `CS0618`s *my own code* introduced — `FadeTo`/`TranslateTo`/`ScaleTo` are obsolete in net10 MAUI (the brief's §9 skeleton predated the `*Async` rename). Incremental builds only recompile changed assemblies, so warnings in untouched code — and in just-edited files once their assembly caches — don't reappear. → [[feedback_verify_warnings_clean_build]] + TD-11.
- **The catalog-refresh failure mid-arc was a red herring** (carried over from the series arc): post-deploy full refresh failed twice then worked; two confident theories (100 s client timeout; stale pooled connection) each killed by one Drew fact. Accepted as TD-A5 (manual re-tap, no auto-retry).

## Lesson

- **Name what your data source can't answer, and arc it separately.** The cache holds only owned books, so "Find" can only ever be "do I own this?" v1. The temptation was to half-build the online "what am I missing?" answers inline; instead we wrote them down (work-level ownership, Missing-bibliography, other-editions), Drew made the explicit call to defer, and they became #54 with graceful-degradation requirements. The brief's §10 "parked" list + a TODO row is the right home — it keeps the shipped surface honest about its scope instead of showing half-wired online affordances.
- **Defer the high-effort review to ARC END for a multi-PR arc.** Mid-arc reviews kept flagging "issues" that were just the next PR's work (transitional pages, a not-yet-shared helper) — false negatives that cost review turns. Drew's call: *"will run the code review at the end of the arc so we stop getting 'thing we are doing in the next PR' false negatives."* Run per-PR reviews only when a PR is independently shippable; for a planned arc, one review at close sees the final state with the scaffolding already gone. → [[feedback_review_at_arc_end]]. (Still gated by [[feedback_review_findings_gate]]: present findings, then WAIT.)
- **A native control's size obeys the layout rect, not its `HeightRequest`.** When a platform view (camera, map, web view) won't bound, stop tuning the requested size and give it a real fixed-rectangle parent (explicit Grid row / fixed-height container + clip). The MAUI element-level lever (`HeightRequest`) is advisory to a native surface; the layout slot is not.
- **Verify warning-clean with `--no-incremental`.** Incremental builds make "0 warnings" meaningless — they don't recompile what didn't change. Before claiming clean, force a full recompile and separate *my* warnings from pre-existing/third-party noise (the Mobile XA0141 16 KB-page-size warnings are NuGet-native, not ours → TD-10). → [[feedback_verify_warnings_clean_build]].
- **A written brief from another agent is a spec to interrogate, not a script to type.** The brief was excellent scaffolding (IA rationale, token set, per-surface states, a motion skeleton), but its §9 code used a since-deprecated API, its "inline camera" needed real native-sizing work the brief couldn't know, and its per-row search stagger would've read as sluggish at 60 rows (I shipped a group reveal instead). Treat brief code as pseudocode; keep its decisions, re-derive its mechanics against the live framework.

## Quotable

> "Going with 2 as the full screen camera for a barcode scan looks super ugly"
>
> — Drew, 2026-06-16, choosing the inline camera — which then turned into the native-surface-sizing saga.

> "I think you only see warnings once as the second build only rebuilds changed assemblies, and the files don't change. Doing a rebuild I get this…"
>
> — Drew, 2026-06-16, correcting my "0 warnings" claim and surfacing 5 CS0618s in my own motion code.

> "merged, will run the code review at the end of the arc so we stop getting 'thing we are doing in the next PR' false negatives."
>
> — Drew, 2026-06-16, setting the arc-end review cadence → [[feedback_review_at_arc_end]].

## Known limitations (recorded, not fixed)

- **F7 / F8 / F10 → TD-12 / TD-13 / TD-14.** SyncChip shows a healthy age even when the launch sync failed/never ran (pairs with #54 auth-expiry); status-rating / cover-fetch / pretty-name presentation helpers are duplicated 2–3×; the SyncChip is Find-only (Wishlist/Gaps use a static ToolbarItem) and camera teardown leans on `OnDisappearing` firing on tab-switch (needs an on-device check).
- **#54 online layer** is the deliberate scope cut, not an oversight — three features that need the web catalogue, all required to degrade gracefully offline and never block the owned-library answer.
- **XA0141 16 KB page sizes (TD-10)** on the ZXing + sqlite native libs — harmless today, but Android 16 will require them; upstream-bump tracking item, and the first concrete reason to pursue TD-11 (warnings-as-errors).
