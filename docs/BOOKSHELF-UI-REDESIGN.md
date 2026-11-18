# Bookshelf (Mobile) — UI Design

**Audience:** Claude Code, working on Bookshelf's UI.
**Scope:** .NET MAUI, `net10.0-android` (Android only; layouts stay platform-agnostic). Offline-first against the local SQLite cache. Read-only app; mutations deep-link to Bookcase (one local-write exception is parked, see §10).

> **How to read this.** This is the design for Bookshelf's UI. Parts of it are built and parts are not; each section says plainly what the app does today (**Built today:**), and the code in `BookTracker.Mobile/` is the ground truth. §1 is the IA decision and its justification. §2 is the token set. §3 is the component kit. §4–§8 are per-surface specs with states and data sources. §9 is motion. §10 is open/parked items.

---

## 1. Locked decisions & IA

### 1.1 Navigation model

Navigation is a **`Shell` with a bottom `TabBar`**, not a `NavigationPage` hub-and-spoke.

**Justification:** the four in-shop tasks are *co-equal and rapidly interleaved against a single physical book* — scan it, check the author's other works, check series gaps, check the want-list, all for the book in hand. Hub-and-spoke makes that loop an in→back→in→back dance. Bottom tabs make each task one tap with no back-stack churn, which is the ergonomic core of the in-shop moment.

**Tabs (3, co-equal):**

| Tab | Icon (Tabler) | Lands as | Covers |
|---|---|---|---|
| **Find** | `ti-search` | default tab | ISBN scan + author search + title search |
| **Wishlist** | `ti-star` | — | the wishlist |
| **Gaps** | `ti-books` | — | series gaps |

- **Do not land on a live camera.** Find opens to a search-ready surface; scanning is one tap from there.
- **Sync is not a tab.** It is an ambient header **chip** (age + entry point) plus a **status sheet** behind it (§8).
- **Gaps is the quiet tab** — kept separate for now, candidate for later deprecation (missing items increasingly flow to Wishlist). Keep it self-contained so removal is cheap.

**Built today:** as above. The tabs are Find (`FindPage`), Wishlist (`WishlistPage`) and Gaps (`SeriesGapsPage`).

### 1.2 Navigation mechanics under Shell

**Built today:** `AppShell.xaml` declares a `TabBar` with three `ShellContent` entries (routes `find`, `wishlist`, `gaps`) and sets the tab-bar colours as attributes on the `Shell` element: `HeaderL/D` background, `BrassTextL/D` for the active icon and title, `OnLeather` / `TextMutedD` for inactive. `AppShell.xaml.cs` takes the three tab pages from DI (they are singletons) and assigns each `ShellContent.Content` in code, because a `DataTemplate` `ContentTemplate` would construct the page without its dependencies; it also sets the tab icons in code as Tabler `FontImageSource`s (`TablerIcons.Search`, `Star`, `Books`).

There are no registered routes and no `GoToAsync`. Drill-downs push onto the active tab's stack with `Navigation.PushAsync` (the tab bar stays visible):

- Find → `ResultPage` (an ISBN typed and submitted, or a barcode scanned).
- Find → `AuthorWorksPage` (tap an author in results).

The status sheet is modal: `StatusSheetPage.OpenAsync` resolves it from DI and calls `Navigation.PushModalAsync`.

---

## 2. Design tokens

Pages use these tokens, not inline hex. All colour pairs resolve via `AppThemeBinding` (in XAML) or `ThemeColors.SetThemeColor` (in code-behind). Spacing rungs (4/6/8/12/14/16/20/24/32), 8 dp corner radius, and touch-target rungs (44/56/64 dp).

### 2.1 `Colors.xaml`

The design token set. `Resources/Styles/Colors.xaml` defines every token below except `CoverTileL/D` and `InfoTxL/D`, which are design-only and not in the file.

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">

  <!-- Mode-stable constants -->
  <Color x:Key="Brass">#A67B3A</Color>        <!-- killer CTA fill, accent borders -->
  <Color x:Key="InkOnBrass">#2C2416</Color>
  <Color x:Key="OnLeather">#FAF6EC</Color>
  <Color x:Key="ScanReticle">#4CAF50</Color>  <!-- do NOT brand-ify -->

  <!-- Surfaces -->
  <Color x:Key="PageBgL">#FAF6EC</Color>      <Color x:Key="PageBgD">#1F140F</Color>
  <Color x:Key="SurfaceL">#F2EADB</Color>     <Color x:Key="SurfaceD">#30211A</Color>
  <Color x:Key="HeaderL">#3E2723</Color>      <Color x:Key="HeaderD">#2A1A13</Color>
  <Color x:Key="InputBgL">#FAF6EC</Color>     <Color x:Key="InputBgD">#2A2018</Color>
  <Color x:Key="CoverTileL">#3E2723</Color>   <Color x:Key="CoverTileD">#1A130D</Color>  <!-- design only -->
  <Color x:Key="CoverMissL">#E2D8C4</Color>   <Color x:Key="CoverMissD">#1A130D</Color>

  <!-- Text -->
  <Color x:Key="TextL">#2C2416</Color>        <Color x:Key="TextD">#F2EADB</Color>
  <Color x:Key="TextMutedL">#6B5D4A</Color>   <Color x:Key="TextMutedD">#B9A98E</Color>

  <!-- Leather primary -->
  <Color x:Key="LeatherL">#6B2737</Color>     <Color x:Key="LeatherD">#8A3447</Color>

  <!-- Brass as text/icon (fill uses constant Brass) -->
  <Color x:Key="BrassTextL">#A67B3A</Color>   <Color x:Key="BrassTextD">#CDA256</Color>

  <!-- Tertiary / progress (green leather) -->
  <Color x:Key="GreenL">#3D5A3F</Color>       <Color x:Key="GreenD">#6E9270</Color>
  <Color x:Key="BarTrackL">#E2D8C4</Color>    <Color x:Key="BarTrackD">#3A2A20</Color>

  <!-- Borders -->
  <Color x:Key="BorderL">#A67B3A</Color>      <Color x:Key="BorderD">#4F3E30</Color>

  <!-- Found (success) card -->
  <Color x:Key="FoundBgL">#E8F5E9</Color>     <Color x:Key="FoundBgD">#1B3320</Color>
  <Color x:Key="FoundBorderL">#A3D9A5</Color> <Color x:Key="FoundBorderD">#356735</Color>
  <Color x:Key="FoundPillBgL">#4F6B3D</Color> <Color x:Key="FoundPillBgD">#2E5A33</Color>
  <Color x:Key="FoundPillTxL">#FAF6EC</Color> <Color x:Key="FoundPillTxD">#BFE3C2</Color>

  <!-- Not-found / neutral card -->
  <Color x:Key="MissBgL">#F0EADF</Color>      <Color x:Key="MissBgD">#2A2018</Color>
  <Color x:Key="MissBorderL">#D8C9A8</Color>  <Color x:Key="MissBorderD">#4F3E30</Color>

  <!-- Status tags -->
  <Color x:Key="OwnedBgL">#DDEBD0</Color>     <Color x:Key="OwnedBgD">#243318</Color>
  <Color x:Key="OwnedTxL">#3B5A1E</Color>     <Color x:Key="OwnedTxD">#8DB36E</Color>
  <Color x:Key="MissTagBgL">#F5DAD5</Color>   <Color x:Key="MissTagBgD">#4A2620</Color>
  <Color x:Key="MissTagTxL">#9B3B2E</Color>   <Color x:Key="MissTagTxD">#D08577</Color>
  <Color x:Key="WarnBgL">#F5E6C8</Color>      <Color x:Key="WarnBgD">#3E2F12</Color>
  <Color x:Key="WarnTxL">#8A6510</Color>      <Color x:Key="WarnTxD">#D6A93F</Color>

  <!-- Sync states -->
  <Color x:Key="OnlineBgL">#E8F1E4</Color>    <Color x:Key="OnlineBgD">#1B3320</Color>
  <Color x:Key="OnlineTxL">#4F6B3D</Color>    <Color x:Key="OnlineTxD">#8DB36E</Color>
  <Color x:Key="InfoTxL">#3A6B7A</Color>      <Color x:Key="InfoTxD">#7FB3C0</Color>      <!-- design only -->
  <Color x:Key="ChipBgL">#EFE4CC</Color>      <Color x:Key="ChipBgD">#30211A</Color>
</ResourceDictionary>
```

### 2.2 `Styles.xaml` (key semantic styles)

**Built today:** `Resources/Styles/Styles.xaml` defines these five styles (its `PrimaryButton` and `OutlinedButton` also set `FontSize` 16 and 14) plus `GapProgress` (a `ProgressBar` style that no page uses; see §10).

```xml
<Style x:Key="Page" TargetType="ContentPage">
  <Setter Property="BackgroundColor"
    Value="{AppThemeBinding Light={StaticResource PageBgL}, Dark={StaticResource PageBgD}}" />
</Style>

<!-- Killer CTA — identical both modes, 64 dp -->
<Style x:Key="KillerButton" TargetType="Button">
  <Setter Property="BackgroundColor" Value="{StaticResource Brass}" />
  <Setter Property="TextColor"       Value="{StaticResource InkOnBrass}" />
  <Setter Property="FontSize" Value="18" /><Setter Property="FontAttributes" Value="Bold" />
  <Setter Property="HeightRequest" Value="64" /><Setter Property="CornerRadius" Value="8" />
</Style>

<!-- Primary CTA — leather brightens in dark, 56 dp -->
<Style x:Key="PrimaryButton" TargetType="Button">
  <Setter Property="BackgroundColor"
    Value="{AppThemeBinding Light={StaticResource LeatherL}, Dark={StaticResource LeatherD}}" />
  <Setter Property="TextColor" Value="{StaticResource OnLeather}" />
  <Setter Property="HeightRequest" Value="56" /><Setter Property="CornerRadius" Value="8" />
</Style>

<!-- Low-emphasis outlined — 44 dp -->
<Style x:Key="OutlinedButton" TargetType="Button">
  <Setter Property="BackgroundColor" Value="Transparent" />
  <Setter Property="BorderColor"
    Value="{AppThemeBinding Light={StaticResource BorderL}, Dark={StaticResource BorderD}}" />
  <Setter Property="BorderWidth" Value="1" />
  <Setter Property="TextColor"
    Value="{AppThemeBinding Light={StaticResource TextMutedL}, Dark={StaticResource TextMutedD}}" />
  <Setter Property="HeightRequest" Value="44" /><Setter Property="CornerRadius" Value="8" />
</Style>

<!-- Base card / passive panel -->
<Style x:Key="Card" TargetType="Border">
  <Setter Property="BackgroundColor"
    Value="{AppThemeBinding Light={StaticResource SurfaceL}, Dark={StaticResource SurfaceD}}" />
  <Setter Property="Stroke"
    Value="{AppThemeBinding Light={StaticResource BorderL}, Dark={StaticResource BorderD}}" />
  <Setter Property="StrokeThickness" Value="1" />
  <Setter Property="Padding" Value="12" />
  <Setter Property="StrokeShape" Value="RoundRectangle 8" />
</Style>
```

### 2.3 Type rungs (explicit `FontSize` per control)

24 page title · 18 killer CTA · 16 standard button / card title (bold) · 15 panel / wishlist title · 14 body / outlined · 13 secondary · 12 microcopy / ISBN (Courier New) · 11 footnotes. Two weights only (Bold for titles & CTAs, Regular otherwise). Fonts are **OpenSans-Regular / OpenSans-Semibold** (registered in `MauiProgram.cs`, alongside the `Tabler` icon font).

### 2.4 Contrast caveats to verify on-device

- Dark mode: `BrassTextD #CDA256` and `TextMutedD #B9A98E` on `SurfaceD #30211A` land ~AA for UI/large; check at 13 px body. If thin, lighten the text rather than darkening the card.
- Never set body text in brass on parchment (fails). Brass is fill / border / icon / ink-on-brass-button only.

---

## 3. Component kit

The design is a kit of reusable `ContentView`s, each consuming §2 tokens only. **Built today:** `BookTracker.Mobile` has no `ContentView` and no `BookRow`, `ResultCard`, `SyncChip`, `Badge` or `EmptyState` type. The button and card entries exist as styles in `Resources/Styles/Styles.xaml` (`PrimaryButton`, `OutlinedButton`, `Card`); the rest are built per page, mostly in code-behind (the rows in `FindPage`, `AuthorWorksPage` and `WishlistPage`, the badges in `WishlistPage`, the progress bars and missing-volume pills in `SeriesGapsPage`) and partly in page XAML (the result cards in `ResultPage.xaml`, the sync chip in `FindPage.xaml`). Extracting the kit is tracked in the backlog.

| Component | Design spec | Built today |
|---|---|---|
| `KillerButton` | Brass fill, ink text, 64 dp, bold 18. Leading Tabler icon. Mode-stable. | Style exists, no consumer. |
| `PrimaryButton` | Leather fill, parchment text, 56 dp. (e.g. "Open in app →", "Refresh now"). | Used by `ResultPage` and `StatusSheetPage`. |
| `OutlinedButton` | Transparent, brass border, muted text, 44 dp. (e.g. "Sign out"). | Used by `StatusSheetPage` (Close, Sign in / Sign out). |
| `BookRow` | Cover tile (placeholder→async image) + title (14–15 bold) + optional author/meta + trailing slot (tag / chevron / action). Used in Recent, search Works, Wishlist, Author-works. | Per-page rows in `FindPage`, `AuthorWorksPage`, `WishlistPage`: cover slot with a `📖` placeholder, title, meta line. No trailing tag or chevron. |
| `ResultCard` | Variants: **Found** (FoundBg/Border, "In your library" pill) and **NotFound** (MissBg/Border, red text pill, no red fill). Cover 64×96, title 18 bold, authors, `★★★★☆ · Read`, mono ISBN. | `FoundFrame` / `MissingFrame` in `ResultPage.xaml`: Found/Miss colours, 80×120 cover, title 18 bold, authors, stars · status, mono ISBN. No pill on either; Not found shows "Not in your library." |
| `WorkEditionPanel` | Card-styled list: header "You own this work as", checked edition rows, and a disabled "Other editions of this work — online only" row (the parked enrichment slot). | Not built. The Found card shows "Works in this book" (only for a multi-Work book) and "Editions you own" (only when more than one is owned). No "online only" row. |
| `Badge` | Pill. Variants: priority **High** (MissTag pair), **Medium** (Warn pair), **Low** (Owned pair); **Series `#N`** (Surface bg, brass border, brass text); **Owned/Missing** tags; **Wishlist** (brass fill, ink). | Priority pills (High/Medium/Low pairs) and a series `#N` pill (ChipBg + brass text, no border) on Wishlist rows. No Owned/Missing or Wishlist badge. |
| `SyncChip` | Header-right pill. States: **synced Xh ago** (ChipBg/muted, `ti-refresh`) and **offline** (MissTag pair, `ti-wifi-off`). Tappable → status sheet. Icon spins while syncing. | Find only: a `Border` in the header (ChipBg, brass stroke) with a label reading `⟳ {age}` online or `⚠ offline` offline, in brass text in both states. Tappable → status sheet. No spin. Wishlist and Gaps have a "Sync" toolbar item that opens the sheet. |
| `ScopeSegment` | Segmented control. Container Surface + brass border; active segment leather + parchment; inactive muted. Used for Find scope (All/Authors/Works) and Author-works (All/Missing only). | Three buttons on Find (All/Authors/Works) and the same shape for the Wishlist sort (Priority/Author/Series): active brass fill + ink, inactive Surface + muted text. Not on Author-works. |
| `ProgressBar` | Track `BarTrack`, fill green-leather. Used in Gaps. | As specified, coloured in `SeriesGapsPage` code-behind. |
| `EmptyState` | Centered muted Tabler glyph (~30 px, 50% opacity) + one line. For zero results / empty wishlist / no gaps. | A centred muted line of text, no glyph. |

**Icons:** the design is the Tabler outline set, not emoji, never mixed on one surface. Star rating stays as `★★★★☆` text. **Built today:** `TablerIcons` carries only the three tab glyphs (`Search`, `Star`, `Books`, used by `AppShell`), and the pages use emoji and symbol characters: the `📖` cover placeholder in `FindPage`, `ResultPage`, `AuthorWorksPage` and `WishlistPage`; `⭐  On your wishlist` in `ResultPage.xaml`; `⟳` / `⚠ offline` on the Find sync chip; `↻  Refresh wishlist` in `WishlistPage.xaml`; `● Online` / `● Offline` in `StatusSheetPage`; and `Synced ✓` in the status lines `SyncService.SyncCatalogAsync` returns. Moving them to Tabler glyphs is tracked in the backlog.

---

## 4. Find — tab default

**Route:** `find`. **Page style:** `Page`. **Header:** espresso, title "Find", trailing `SyncChip`.

### 4.1 State: ready (no query)
- Unified search field (`InputBg`, 1.5 px brass border, `ti-search` leading, brass scan button `ti-barcode` trailing 40×40). Placeholder "Title, author or ISBN…". Auto-focus.
- **Recent** list — last few look-ups as `BookRow`s tagged in-library (Owned) / not (Missing). **Parked** (§10): Find opens to an empty search-ready state.
- **No "updates available" prompt** (peeking the delta costs ~the same as syncing). Sync is auto and/or manual-via-status-sheet.

**Built today:** the header, the field (`InputBg`, 1.5 px brass border, no leading icon) with the placeholder above and auto-focus, and a brass text button "Scan" (48 dp, no icon) that opens an inline camera strip (220 dp, with a green `ScanReticle` outline) and changes to "Cancel" while it is open. No Recent list.

### 4.2 State: live results
Debounced (250 ms / 2-char floor, cancellation-guarded). Results **grouped** + a **`ScopeSegment` (All · Authors · Works)** directly under the field, default **All**:
- **Authors · N** group → rows: initials avatar (leather) + name + "N books" + chevron → `find/author`.
- **Works · N** group → `BookRow`s with Owned/Missing tag → `find/result`.
- **ISBN auto-detect:** a purely numeric input (10/13 digits, optional check-digit) **skips grouping** and goes straight to `find/result`. ISBN is therefore *not* a fourth segment.
- Scope filter re-scopes results instantly (no re-query needed if both already fetched).
- **Data:** `SearchAuthors` (Authors group), `SearchBooksByTitle` (Works group), `LookupByIsbn` (numeric path).

**Built today:** debounce, floor, grouping, scope segment and data sources as above (up to 30 results per group). Author rows are cards with the name and "N books", no avatar or chevron, and push `AuthorWorksPage`. Work rows are cards with a cover, title and stars · status line, no tag (every hit is owned), and a tap opens the book's web page (`/books/{id}`) in the browser rather than `ResultPage`. An input of 10–13 digits (an `X` allowed last) shows "Press Enter to look up this ISBN." and pushes `ResultPage` on Enter; a scanned barcode pushes it directly.

### 4.3 State: result (`ResultPage`)
- **Found:** `ResultCard` (Found) + `WorkEditionPanel` + `PrimaryButton` "Open in app →" (deep-link `/books/{id}`).
  - Answer at **work** level: "In your library" is true if the *work* is owned in any edition, even when the scanned ISBN is a different printing. The owned-edition(s) are listed; the unscanned/unowned editions sit in the disabled "online only" row.
  - **Data:** `GetBookEnrichedDetail` (works + editions), `IsWishlistedIsbn` (wishlist flag, may pop in late — never block the card on it).
- **Not found:** `ResultCard` (NotFound) + line "You don't own this work in any edition." + **`Badge`/Wishlist button "Add to wishlist"** (brass — **inert**, see §10) + `PrimaryButton` "Add in app →" (deep-link to web add-book with ISBN prefilled).

**Built today:** the answer is at **edition** level — `LookupByIsbn` against the cache, so a different printing of an owned work reads as not found (the work-level answer needs the online layer, §10). Found shows the Found card with "Open in app →", plus "Works in this book" and "Editions you own" lists when there is more than one of each (`GetBookEnrichedDetail`). Not found shows "Not in your library.", the ISBN and "Add in app →". A `⭐  On your wishlist` banner appears above either card when `IsWishlistedIsbn` is true. There is no "Add to wishlist" button and no "online only" row.

---

## 5. Author → works (`AuthorWorksPage`)

The screen that answers "what does this author have, and do I own it?" — one of the four core in-shop questions.

- **Header:** back + author name.
- **Summary:** "N works · M owned".
- **`ScopeSegment` (All · Missing only)** — "Missing only" is the in-shop killer: one tap to just what you don't own by this author.
- **Rows (`BookRow`):**
  - Owned → `★★★★☆ · Read` meta + Owned tag.
  - Not owned → muted (≈80% opacity), "Not owned" + a brass `ti-star` quick action (the **same inert Add-to-wishlist** as §4.3 / §10 — wiring it lights up everywhere at once).
- **Data:** `LookupByAuthor`. Pen-name aliases roll up to the canonical author (hint text explains it).

**Built today:** owned books only, since the cache holds only the owned library (the not-owned rows and the Missing scope need the online layer, §10). The page title is the author's name; the header line reads "N books in your library"; each row is a card with a cover, title and stars · status line, and a tap opens the book's web page. No scope segment, no tag, no star action. `LookupByAuthor` is called with the canonical author id, so alias credits roll up; there is no hint text.

---

## 6. Wishlist — tab

**Route:** `wishlist`. Framed for **proactive recall** (scenario 2: "what am I after today" / shop-owner conversations), not just reactive flagging.

- **Header:** "Wishlist" + `SyncChip`.
- **Controls:** count + a sort control (`ti-arrows-sort`). Default sort **Priority** with light group headers (High / Medium / Low); **Author** and **Series order** are the alternate sorts.
- **Rows (`BookRow`):** cover 36×52 + title 14 bold + author 12 + `Badge`s (priority pill + optional series `#N`) + **Bought** button (brass, 40 dp).
- **Bought** sets the **local** flag — hides the row, survives cache refresh, reconciles on sync. Animate the collapse (§9).
- **Empty:** `EmptyState`.
- **Data:** wishlist snapshot read + local bought flag. Refresh pulls `/api/wishlist-snapshot`.

**Built today:** the page title "Wishlist" with a "Sync" toolbar item (no chip); a leather "↻  Refresh wishlist" button that pulls `/api/wishlist-snapshot` into the cache; a count line; a three-button sort segment (Priority / Author / Series, default Priority with High / Medium / Low headers). Rows: cover 48×68, title 15 bold, author 13, priority and series badges, and a brass "Bought" button (40 dp) that fades the row out, sets the local bought flag and reloads. Empty: a line of text.

---

## 7. Gaps — tab (quiet)

**Route:** `gaps`. Read-only.

- **Header:** "Gaps" + `SyncChip`. Summary "N series with gaps".
- **One card per series:** name (16 bold) + progress "X of Y owned" + **`ProgressBar`** + missing volumes:
  - Numbered as leather `vol` pills: `#3`, `#7`.
  - Runs collapse: `#5–#6`.
  - **Interquels render with their display label in muted ink**, never a numbered pill: `Dawnshard (#4.5)` — must not appear to claim a numbered slot.
- **Empty:** `EmptyState` ("No gaps — you're complete").
- **Data:** `GetSeriesGaps`.

**Built today:** the page title "Series gaps" with a "Sync" toolbar item (no chip); the summary line; one `Card` per series with name, "X of Y owned", the progress bar and a "Missing" header over pills. Pills are Surface with a brass border and brass text; a run of three or more missing slots collapses to one `#N–#M` pill (`SeriesGapFormatter.MissingParts`). Missing slots are whole numbers, so interquels never appear. Empty: a line of text.

---

## 8. Status sheet (modal, behind the SyncChip)

The home for sync detail; **not a tab**. Presented modally.

- **Connectivity row:** Online (Online pair, `ti-wifi`) / Offline (MissTag pair, `ti-wifi-off`) + sub-label.
- **Stats card:** Last synced (age) · Books cached · Authors · Editions · Wishlist count. **No pre-counted "pending updates"** — the count is reported *after* a refresh as a result ("synced · 12 new items"), not polled ahead of time.
- **`PrimaryButton` "Refresh now"** (56 dp) — fires the `?since` watermark delta-sync.
- **Footer:** build/version (11 px).
- **Data:** `GetMeta` (counts + last-synced), `Connectivity` (state).

**Built today:** `StatusSheetPage` has a "Sync & account" title with a Close button; a `● Online` / `● Offline` line in the OnlineTx / MissTagTx colour; a "Cached catalog" card showing books and authors counts and "Last synced {age}"; "Refresh now" (enabled when signed in and online), which runs `SyncService.SyncCatalogAsync` and shows its result line (full, or delta with updated and removed counts); a Sign in / Sign out button; and the build footer.

### 8.1 Sync behaviour
- **On start:** `Connectivity.Current.NetworkAccess == Internet` → fire the delta sync silently in the background. No prompt.
- **While open:** subscribe to `ConnectivityChanged` → flip `SyncChip` between "synced Xh ago" and "offline".

**Built today:** `AppShell.OnAppearing` runs once per launch: it opens the cache, refreshes sign-in state and meta, and when the user is signed in and online calls `SyncCatalogAsync`, swallowing any failure. `SyncService` subscribes to `Connectivity.ConnectivityChanged`, updates `IsOnline` and raises `StateChanged`, which the Find chip listens to.

---

## 9. Motion layer

**Principle: motion decorates the result, never gates it.** Animate containers *as* data binds, so a fast tap still feels instant. Durations 150–220 ms, decelerating, skippable. No third-party lib — `ViewExtensions` + the `Animation` class cover everything.

**Built today:** `Theming/Motion.cs` provides `InAsync` (fade + rise) and `PulseAsync` (scale pulse), both no-ops when `Motion.Enabled` is false:

```csharp
public static class Motion
{
    public static bool Enabled = true; // set by the reduced-motion guard

    public static Task InAsync(this VisualElement v, uint ms = 190, double rise = 12)
    {
        if (!Enabled) { v.Opacity = 1; v.TranslationY = 0; return Task.CompletedTask; }
        v.Opacity = 0; v.TranslationY = rise;
        return Task.WhenAll(
            v.FadeToAsync(1, ms, Easing.CubicOut),
            v.TranslateToAsync(0, 0, ms, Easing.CubicOut));
    }
}
```

| Moment | Design treatment | Built today |
|---|---|---|
| **Result reveal** | `await resultCard.InAsync();` right after binding — answer + motion land together. | `ResultPage` calls `InAsync` on the Found or Not-found card. |
| **Cover cross-fade** | `cover.Opacity = 0; cover.Source = src; await cover.FadeTo(1, 250, Easing.CubicInOut);` — dissolves over the placeholder. | Not built: the image replaces the placeholder without a fade. |
| **Barcode recognised** | `HapticFeedback.Default.Perform(HapticFeedbackType.Click);` then reticle pulse `ScaleTo(1.06,90,CubicOut)`→`ScaleTo(1.0,110,CubicIn)`. Fire *before* the result renders. | As designed, in `FindPage.OnBarcodesDetected` (`PulseAsync`). |
| **Tab cross-fade** | `await ContentRoot.InAsync(rise: 6)` in each tab's `OnAppearing` — softens Shell's instant tab switch. | As designed on all three tabs; `AuthorWorksPage` also fades in on push. |
| **Incremental search** | per row `row.InAsync(rise: 8)` with `Task.Delay(i * 25)` stagger as debounced results stream in. | The whole results list fades in once (`ResultsLayout.InAsync(rise: 8)`); no per-row stagger. |
| **Refresh spin** | `new Animation(v => icon.Rotation = v, 0, 360).Commit(icon, "spin", length: 800, repeat: () => IsSyncing);` | Not built: the status sheet shows an `ActivityIndicator` while busy. |
| **Bought toggle** | collapse (`FadeTo(0)` + height→0) instead of yanking the row. | The row fades out (160 ms) before the list reloads; no height collapse. |

**Reduced-motion guard** (Android system animator scale; 0 ⇒ off), built in `Platforms/Android/MainActivity.OnCreate`:

```csharp
var scale = Settings.Global.GetFloat(ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
Theming.Motion.Enabled = scale > 0f;
```

**Caveat:** Shell page-level push/pop transitions are not very customisable in MAUI — element-level entrances above are the deliberate, pragmatic route.

---

## 10. Open / parked items & decisions

| Item | State | Notes |
|---|---|---|
| **Add to wishlist (local write)** | **Parked — not built** | Designed for Not-found (§4.3) and Author-works missing rows (§5); no button exists today. Wire as a local write like the Bought flag (survives refresh, reconciles on sync), in all locations at once. |
| **Online edition lookup** | **Parked** | The "Other editions of this work — online only" row in `WorkEditionPanel` (not built). When online, resolves an unknown ISBN against the web catalogue → "you have this *work*, not this *edition*". Must degrade gracefully to "online only" offline; never blocks the primary answer. |
| **Unconsumed styles and tokens** | **Parked** | `Styles.xaml` defines `KillerButton` and `GapProgress`, and `Colors.xaml` defines `FoundPillBgL/D`, `FoundPillTxL/D` and `OnlineBgL/D`, with no consumer (`SeriesGapsPage` colours its progress bars in code-behind instead of using `GapProgress`). `TablerIcons` holds only the three tab glyphs; the full Tabler font is bundled, so other glyphs are a one-line addition. They stay for the surfaces §3 specifies; delete any that the component kit does not end up using. |
| **Gaps tab longevity** | **Watch** | Candidate for later deprecation as missing→wishlist grows. Keep self-contained. |
| **Recent-lookups list** (§4.1) | **Parked** | Find opens to an empty search-ready state. Adding it needs a lookup-history store (in-memory resets on restart; persisted = new SQLite table). |
| **Online layer (work-level ownership · author Missing · other editions)** | **Parked — TODO #54** | The cache holds only the owned library, so all "what am I *missing*" features need online catalogue data. Find is offline search/scan of owned items; the online layer is a separate arc. |

---

## 11. References

- Ground truth: `BookTracker.Web/Theme/BookTrackerTheme.cs`, `BookTracker.Mobile/**`.
- Shell tabs: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/tabs>
- Shell navigation/routes: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation>
- Theming / `AppThemeBinding`: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/system-theme-changes>
- Basic animations: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/basic>
- Easing: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/easing>
- Custom (`Animation` class): <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/custom>
- Haptics: <https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/haptic-feedback>
- Connectivity: <https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/networking>
