# Bookshelf (Mobile) — UI Redesign Brief

**Audience:** Claude Code, implementing the redesign.
**Status:** Design-locked from the planning session of 2026-06. Supersedes the *visual/IA* portions of `docs/MOBILE-UI-SPEC.md`; the hard constraints in that doc's §2 still hold except the one deliberate deviation noted in §1 below.
**Scope:** .NET MAUI, `net10.0-android`. Read-only app; mutations deep-link to Bookcase (one new local-write exception is parked, see §9).

> **How to read this.** §1 is the locked IA decision (incl. the one constraint break and its justification). §2 is the token set — implement first. §3 is the component kit. §4–§8 are per-surface specs with states and data sources. §9 is motion. §10 is open/parked items. §11 is suggested PR sequencing.

---

## 1. Locked decisions & IA

### 1.1 Navigation model — **constraint deviation, justified**

The current `NavigationPage` hub-and-spoke is replaced by a **`Shell` with a bottom `TabBar`**. This is the single deviation from the original §2 ("`NavigationPage` stack — *not* Shell").

**Justification (the "good reason"):** the four in-shop tasks are *co-equal and rapidly interleaved against a single physical book* — scan it, check the author's other works, check series gaps, check the want-list, all for the book in hand. Hub-and-spoke makes that loop an in→back→in→back dance. Bottom tabs make each task one tap with no back-stack churn, which is the ergonomic core of the in-shop moment.

**Tabs (3, co-equal):**

| Tab | Icon (Tabler) | Lands as | Subsumes |
|---|---|---|---|
| **Find** | `ti-search` | default tab | old ScanPage + AuthorSearchPage + TitleSearchPage |
| **Wishlist** | `ti-star` | — | old WishlistPage |
| **Gaps** | `ti-books` | — | old SeriesGapsPage |

- **Do not land on a live camera.** Find opens to a search-ready surface; scanning is one tap from there.
- **Sync is not a tab.** It is an ambient header **chip** (age + entry point) plus a **status sheet** behind it (§8).
- **Gaps is the quiet tab** — kept separate for now, candidate for later deprecation (missing items increasingly flow to Wishlist). Keep it self-contained so removal is cheap.

### 1.2 Navigation mechanics under Shell

```xml
<!-- AppShell.xaml -->
<Shell ... FlyoutBehavior="Disabled">
  <TabBar>
    <ShellContent Title="Find"     Icon="tab_find.png"     Route="find"     ContentTemplate="{DataTemplate p:FindPage}" />
    <ShellContent Title="Wishlist" Icon="tab_wishlist.png" Route="wishlist" ContentTemplate="{DataTemplate p:WishlistPage}" />
    <ShellContent Title="Gaps"     Icon="tab_gaps.png"     Route="gaps"     ContentTemplate="{DataTemplate p:GapsPage}" />
  </TabBar>
</Shell>
```

Drill-downs push **within the active tab's stack** (the tab bar stays visible), via registered routes + `GoToAsync`:

- `find` → `find/result` (ISBN/title resolved to a single book) — `ResultPage`
- `find` → `find/author` (tap an author in results) — `AuthorWorksPage`
- Status sheet is **modal**: `GoToAsync("status")` registered as a modal route (or `Navigation.PushModalAsync`).

```csharp
Routing.RegisterRoute("find/result", typeof(ResultPage));
Routing.RegisterRoute("find/author", typeof(AuthorWorksPage));
Routing.RegisterRoute("status",      typeof(StatusSheetPage)); // presented modally
```

Tab-bar styling (espresso bar, brass active, muted inactive):

```xml
<Style TargetType="TabBar">
  <Setter Property="Shell.TabBarBackgroundColor"
    Value="{AppThemeBinding Light={StaticResource HeaderL}, Dark={StaticResource HeaderD}}" />
  <Setter Property="Shell.TabBarForegroundColor"
    Value="{AppThemeBinding Light={StaticResource BrassTextL}, Dark={StaticResource BrassTextD}}" />
  <Setter Property="Shell.TabBarUnselectedColor"
    Value="{AppThemeBinding Light={StaticResource OnLeather}, Dark={StaticResource TextMutedD}}" />
</Shell>
```

---

## 2. Design tokens (implement first)

Replaces the hand-mirrored inline hex (closes old TODO #37 and gaps §8.1/§8.7). All colour pairs resolve via `AppThemeBinding`. Spacing rungs (4/6/8/12/14/16/20/24/32), 8 dp corner radius, and touch-target rungs (44/56/64 dp) are **unchanged** from the original spec.

### 2.1 `Colors.xaml`

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
  <Color x:Key="CoverTileL">#3E2723</Color>   <Color x:Key="CoverTileD">#1A130D</Color>
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
  <Color x:Key="InfoTxL">#3A6B7A</Color>      <Color x:Key="InfoTxD">#7FB3C0</Color>
  <Color x:Key="ChipBgL">#EFE4CC</Color>      <Color x:Key="ChipBgD">#30211A</Color>
</ResourceDictionary>
```

### 2.2 `Styles.xaml` (key semantic styles)

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

### 2.3 Type rungs (unchanged, explicit `FontSize` per control)

24 page title · 18 killer CTA · 16 standard button / card title (bold) · 15 panel / wishlist title · 14 body / outlined · 13 secondary · 12 microcopy / ISBN (Courier New) · 11 footnotes. Two weights only (Bold for titles & CTAs, Regular otherwise). Fonts remain **OpenSans-Regular / OpenSans-Semibold**.

### 2.4 Contrast caveats to verify on-device

- Dark mode: `BrassTextD #CDA256` and `TextMutedD #B9A98E` on `SurfaceD #30211A` land ~AA for UI/large; check at 13 px body. If thin, lighten the text rather than darkening the card.
- Never set body text in brass on parchment (fails). Brass is fill / border / icon / ink-on-brass-button only.

---

## 3. Component kit

Build these as reusable `ContentView`s (closes gap §8.3 — no more imperative code-behind cards). Every one consumes §2 tokens only.

| Component | Spec |
|---|---|
| `KillerButton` | Brass fill, ink text, 64 dp, bold 18. Leading Tabler icon. Mode-stable. |
| `PrimaryButton` | Leather fill, parchment text, 56 dp. (e.g. "Open in app →", "Refresh now"). |
| `OutlinedButton` | Transparent, brass border, muted text, 44 dp. (e.g. "Sign out"). |
| `BookRow` | Cover tile (placeholder→async image) + title (14–15 bold) + optional author/meta + trailing slot (tag / chevron / action). Used in Recent, search Works, Wishlist, Author-works. |
| `ResultCard` | Variants: **Found** (FoundBg/Border, "In your library" pill) and **NotFound** (MissBg/Border, red text pill, no red fill). Cover 64×96, title 18 bold, authors, `★★★★☆ · Read`, mono ISBN. |
| `WorkEditionPanel` | Card-styled list: header "You own this work as", checked edition rows, and a disabled "Other editions of this work — online only" row (the parked enrichment slot). |
| `Badge` | Pill. Variants: priority **High** (MissTag pair), **Medium** (Warn pair), **Low** (Owned pair); **Series `#N`** (Surface bg, brass border, brass text); **Owned/Missing** tags; **Wishlist** (brass fill, ink). |
| `SyncChip` | Header-right pill. States: **synced Xh ago** (ChipBg/muted, `ti-refresh`) and **offline** (MissTag pair, `ti-wifi-off`). Tappable → status sheet. Icon spins while syncing. |
| `ScopeSegment` | Segmented control. Container Surface + brass border; active segment leather + parchment; inactive muted. Used for Find scope (All/Authors/Works) and Author-works (All/Missing only). |
| `ProgressBar` | Track `BarTrack`, fill green-leather. Used in Gaps. |
| `EmptyState` | Centered muted Tabler glyph (~30 px, 50% opacity) + one line. For zero results / empty wishlist / no gaps (closes gap §8.4). |

**Icons:** replace all emoji with the Tabler outline set, one surface at a time, never mixed (gap §8.2). Star rating stays as `★★★★☆` text.

---

## 4. Find — tab default

**Route:** `find`. **Page style:** `Page`. **Header:** espresso, title "Find", trailing `SyncChip`.

### 4.1 State: ready (no query)
- Unified search field (`InputBg`, 1.5 px brass border, `ti-search` leading, brass scan button `ti-barcode` trailing 40×40). Placeholder "Title, author or ISBN…". Auto-focus.
- **Recent** list — last few look-ups as `BookRow`s tagged in-library (Owned) / not (Missing).
- **No "updates available" prompt** — explicitly removed (peeking the delta costs ~the same as syncing). Sync is auto and/or manual-via-status-sheet.

### 4.2 State: live results
Debounced (250 ms / 2-char floor, cancellation-guarded — keep existing behaviour). Results **grouped** + a **`ScopeSegment` (All · Authors · Works)** directly under the field, default **All**:
- **Authors · N** group → rows: initials avatar (leather) + name + "N books" + chevron → `find/author`.
- **Works · N** group → `BookRow`s with Owned/Missing tag → `find/result`.
- **ISBN auto-detect:** a purely numeric input (10/13 digits, optional check-digit) **skips grouping** and goes straight to `find/result`. ISBN is therefore *not* a fourth segment.
- Scope filter re-scopes results instantly (no re-query needed if both already fetched).
- **Data:** `SearchAuthors` (Authors group), `SearchBooksByTitle` (Works group), `LookupByIsbn` (numeric path).

### 4.3 State: result (`find/result` → `ResultPage`)
- **Found:** `ResultCard` (Found) + `WorkEditionPanel` + `PrimaryButton` "Open in app →" (deep-link `/books/{id}`).
  - Answer at **work** level: "In your library" is true if the *work* is owned in any edition, even when the scanned ISBN is a different printing. The owned-edition(s) are listed; the unscanned/unowned editions sit in the disabled "online only" row.
  - **Data:** `GetBookEnrichedDetail` (works + editions), `IsWishlistedIsbn` (wishlist flag, may pop in late — never block the card on it).
- **Not found:** `ResultCard` (NotFound) + line "You don't own this work in any edition." + **`Badge`/Wishlist button "Add to wishlist"** (brass — **inert for v1**, see §10) + `PrimaryButton` "Add in app →" (deep-link to web add-book with ISBN prefilled).

---

## 5. Author → works (`find/author` → `AuthorWorksPage`)

The screen that answers "what does this author have, and do I own it?" — one of the four core in-shop questions.

- **Header:** back + author name.
- **Summary:** "N works · M owned".
- **`ScopeSegment` (All · Missing only)** — "Missing only" is the in-shop killer: one tap to just what you don't own by this author.
- **Rows (`BookRow`):**
  - Owned → `★★★★☆ · Read` meta + Owned tag.
  - Not owned → muted (≈80% opacity), "Not owned" + a brass `ti-star` quick action (the **same inert Add-to-wishlist** as §4.3 / §10 — wiring it lights up everywhere at once).
- **Data:** `LookupByAuthor`. Pen-name aliases roll up to the canonical author (keep existing behaviour; hint text explains it).

---

## 6. Wishlist — tab

**Route:** `wishlist`. Framed for **proactive recall** (scenario 2: "what am I after today" / shop-owner conversations), not just reactive flagging.

- **Header:** "Wishlist" + `SyncChip`.
- **Controls:** count + a sort control (`ti-arrows-sort`). *Proposal* (layout change from §5.6 flat list): default sort **Priority** with light group headers (High / Medium / Low). Offer **Author** and **Series order** as alternate sorts — Author/Series is likely the better default for the shop-owner case; confirm preferred default.
- **Rows (`BookRow`):** cover 36×52 + title 14 bold + author 12 + `Badge`s (priority pill + optional series `#N`) + **Bought** button (brass, 40 dp).
- **Bought** sets the existing **local** flag — hides the row, survives cache refresh, reconciles on sync. Animate the collapse (§9).
- **Empty:** `EmptyState`.
- **Data:** wishlist snapshot read + local bought flag (existing). Refresh pulls `/api/wishlist-snapshot`.

---

## 7. Gaps — tab (quiet)

**Route:** `gaps`. Read-only.

- **Header:** "Gaps" + `SyncChip`. Summary "N series with gaps".
- **One card per series:** name (16 bold) + progress "X of Y owned" + **`ProgressBar`** (*proposal*, scannability improvement over §5.5/§8.5) + missing volumes:
  - Numbered as leather `vol` pills: `#3`, `#7`.
  - Runs collapse: `#5–#6`.
  - **Interquels render with their display label in muted ink**, never a numbered pill: `Dawnshard (#4.5)` — must not appear to claim a numbered slot.
- **Empty:** `EmptyState` ("No gaps — you're complete").
- **Data:** `GetSeriesGaps`.

---

## 8. Status sheet (modal, behind the SyncChip)

**Route:** `status` (modal). The home for sync detail; **not a tab**.

- **Connectivity row:** Online (Online pair, `ti-wifi`) / Offline (MissTag pair, `ti-wifi-off`) + sub-label.
- **Stats card:** Last synced (age) · Books cached · Authors · Editions · Wishlist count. **No pre-counted "pending updates"** — the count is reported *after* a refresh as a result ("synced · 12 new items"), not polled ahead of time.
- **`PrimaryButton` "Refresh now"** (56 dp) — fires the existing `?since` watermark delta-sync.
- **Footer:** build/version (11 px).
- **Data:** `GetMeta` (counts + last-synced), `Connectivity` (state).

### 8.1 Sync behaviour
- **On start:** `Connectivity.Current.NetworkAccess == Internet` → trigger the chosen sync mode. **Open decision (§10):** auto-sync silently, or leave to manual refresh. Either way, no prompt.
- **While open:** subscribe to `ConnectivityChanged` → flip `SyncChip` between "synced Xh ago" and "offline".

```csharp
Connectivity.Current.ConnectivityChanged += (_, e) =>
    _state.IsOnline = e.NetworkAccess == NetworkAccess.Internet;
```

---

## 9. Motion layer

**Principle: motion decorates the result, never gates it.** Animate containers *as* data binds, so a fast tap still feels instant. Durations 150–220 ms, decelerating, skippable. No third-party lib — `ViewExtensions` + the `Animation` class cover everything.

```csharp
public static class Motion
{
    public static bool Enabled = true; // set by the reduced-motion guard

    public static Task InAsync(this VisualElement v, uint ms = 190, double rise = 12)
    {
        if (!Enabled) { v.Opacity = 1; v.TranslationY = 0; return Task.CompletedTask; }
        v.Opacity = 0; v.TranslationY = rise;
        return Task.WhenAll(
            v.FadeTo(1, ms, Easing.CubicOut),
            v.TranslateTo(0, 0, ms, Easing.CubicOut));
    }
}
```

| Moment | Treatment |
|---|---|
| **Result reveal** | `await resultCard.InAsync();` right after binding — answer + motion land together. |
| **Cover cross-fade** | `cover.Opacity = 0; cover.Source = src; await cover.FadeTo(1, 250, Easing.CubicInOut);` — dissolves over the placeholder (upgrades the §6 async-cover swap). |
| **Barcode recognised** | `HapticFeedback.Default.Perform(HapticFeedbackType.Click);` then reticle pulse `ScaleTo(1.06,90,CubicOut)`→`ScaleTo(1.0,110,CubicIn)`. Fire *before* the result renders. |
| **Tab cross-fade** | `await ContentRoot.InAsync(rise: 6)` in each tab's `OnAppearing` — softens Shell's instant tab switch. |
| **Incremental search** | per row `row.InAsync(rise: 8)` with `Task.Delay(i * 25)` stagger as debounced results stream in. |
| **Refresh spin** | `new Animation(v => icon.Rotation = v, 0, 360).Commit(icon, "spin", length: 800, repeat: () => IsSyncing);` |
| **Bought toggle** | collapse (`FadeTo(0)` + height→0) instead of yanking the row. |

**Reduced-motion guard** (Android system animator scale; 0 ⇒ off):

```csharp
// Platforms/Android/MainActivity, once at startup
var scale = Settings.Global.GetFloat(ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
Motion.Enabled = scale > 0f;
```

**Caveat:** Shell page-level push/pop transitions are not very customisable in MAUI — element-level entrances above are the deliberate, pragmatic route.

---

## 10. Open / parked items & decisions

| Item | State | Notes |
|---|---|---|
| **Add to wishlist (local write)** | **Parked — button present but inert** | Appears on Not-found (§4.3) and Author-works missing rows (§5). Wire as a local write like the Bought flag (survives refresh, reconciles on sync). When implemented, lights up in all locations at once. No layout change needed to activate. |
| **Online edition lookup** | **Parked** | The disabled "Other editions of this work — online only" row in `WorkEditionPanel`. When online, resolves an unknown ISBN against the web catalogue → "you have this *work*, not this *edition*". Must degrade gracefully to "online only" offline; never blocks the primary answer. |
| **Sync mode on start** | **Decided (2026-06-16): auto-sync silently** | When online at launch, fire the delta sync in the background — no prompt. Wire in PR4. |
| **Wishlist default sort** | **Decided (2026-06-16): Priority default** | Keep High/Medium/Low grouping as default; offer Author/Series as alternate sorts. PR4. |
| **Gaps tab longevity** | **Watch** | Candidate for later deprecation as missing→wishlist grows. Keep self-contained. |
| **Recent-lookups list** (§4.1) | **Deferred from v1 — decide this arc or push (2026-06-16)** | Find opens to an empty search-ready state for now. Adding it needs a lookup-history store (in-memory resets on restart; persisted = new SQLite table). Decide before the arc closes whether it earns a slot or moves to TODO. |
| **Online layer (work-level ownership · author Missing · other editions)** | **Deferred to its own enhancement arc — TODO #54 (2026-06-16)** | The cache holds only the owned library, so all "what am I *missing*" features need online catalogue data. v1 Find is offline search/scan of owned items; the online layer is a separate arc. |
| **Transitional stragglers — recheck at arc end** | **Accepted as transitional (2026-06-16 arc review F3/F4/F5)** | (F3) `MainPage`/`ScanPage`/Author/Title still light-only inline hex → dark mode looks broken on the default Find tab + pushed search pages until PR3 rebuilds them. (F4) `KillerButton`/`PrimaryButton`/`OutlinedButton`/`GapProgress` styles + (F5) ~24 tokens and 13/16 `TablerIcons` glyphs have no consumers yet (defined ahead of the surfaces that use them; icon codepoints unverified-until-rendered). **All deliberately deferred** — the surfaces that consume them are built in PR3–5. **At end of arc, recheck:** any page still on inline hex, any style/token/glyph still unconsumed → migrate or delete. Don't let the foundation's unused half calcify. |

---

## 11. Suggested PR sequencing

1. **PR 1 — Tokens + component kit.** `Colors.xaml` / `Styles.xaml` (§2) and the `ContentView` kit (§3). No behaviour change; existing pages can adopt incrementally. Closes old TODO #37 and gaps §8.1/§8.3/§8.7.
2. **PR 2 — Shell migration.** `NavigationPage` → `Shell` + `TabBar` + routes (§1.2). IA change; drill-downs become registered routes.
3. **PR 3 — Find rework + Author-works.** Unified search, grouped results + `ScopeSegment`, ISBN auto-detect, `ResultCard` + `WorkEditionPanel`, `AuthorWorksPage` with All/Missing filter (§4–§5).
4. **PR 4 — Wishlist / Gaps / Status + ambient sync.** Sort+grouping, `ProgressBar` + run/interquel rendering, status sheet, `SyncChip` + connectivity check, **remove the update prompt** (§6–§8).
5. **PR 5 — Motion + dark completion.** Motion layer + reduced-motion guard (§9); verify dark mode across *all* pages (closes gap §8.1).
6. **Later** — live Add-to-wishlist write; online edition enrichment (§10).

---

## 12. References

- Ground truth: `docs/MOBILE-UI-SPEC.md`, `BookTracker.Web/Theme/BookTrackerTheme.cs`, `BookTracker.Mobile/**`.
- Shell tabs: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/tabs>
- Shell navigation/routes: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation>
- Theming / `AppThemeBinding`: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/system-theme-changes>
- Basic animations: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/basic>
- Easing: <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/easing>
- Custom (`Animation` class): <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/animation/custom>
- Haptics: <https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/haptic-feedback>
- Connectivity: <https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/networking>
