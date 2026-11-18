# BookTracker style guide

Visual and interaction conventions covering both surfaces of the product:

- **Web** — `BookTracker.Web` (Blazor Web App, MudBlazor + Bootstrap holdouts during the convert-as-we-touch migration).
- **Mobile** — `BookTracker.Mobile` (.NET MAUI Android companion).

This document **describes** what's already in the code. The canonical source of truth for theme values is `BookTracker.Web/Theme/BookTrackerTheme.cs` for Web and the design tokens in `BookTracker.Mobile/Resources/Styles/Colors.xaml` for Mobile, with the semantic styles built on them in `Resources/Styles/Styles.xaml` (the Mobile values mirror the Web values manually — see [Known inconsistencies](#known-inconsistencies) for the long-term plan to close that gap). When something here disagrees with the code, the code wins; the doc was the one that got stale.

**Update convention.** When a UI change introduces a new colour, font size, spacing value, component pattern, or layout shape — update this doc in the same PR. The `convert-as-we-touch` discipline applies here too: don't pre-emptively rewrite the doc to match a future ideal; do update it when you're touching the thing it describes.

## Contents

1. [Scope & intent](#scope--intent)
2. [Palette](#palette)
3. [Typography](#typography)
4. [Spacing & sizing](#spacing--sizing)
5. [Component patterns](#component-patterns)
6. [Layout patterns](#layout-patterns)
7. [Iconography](#iconography)
8. [Accessibility](#accessibility)
9. [Platform-specific quirks](#platform-specific-quirks)
10. [Known inconsistencies](#known-inconsistencies)

## Scope & intent

BookTracker is one product across two surfaces. A user with both installed should feel the visual and interaction continuity — the cream-and-leather palette, the espresso header bar, the brass accents on primary in-bookshop actions. Mobile isn't a sibling brand; it's the same brand in a different container.

What this guide is **not**: an exhaustive design system. There's no published component library or token registry yet — BookTracker is one developer's personal project. The conventions captured here are the ones that exist *de facto* in the code; the guide makes them explicit so they don't drift the next time something is touched without the other side getting the same treatment.

## Palette

The library palette — leather spines, brass fixtures, parchment pages, espresso ink. Mirrored from `BookTracker.Web/Theme/BookTrackerTheme.cs:11–52`.

Mobile names the light-mode value of each colour as a token in `BookTracker.Mobile/Resources/Styles/Colors.xaml` (each `…L` token has a `…D` dark-mode partner).

| Name | Hex | Semantic role | Web token | Mobile token and usage |
|---|---|---|---|---|
| Leather | `#6B2737` | Primary actions, primary text accent (e.g. panel headings) | `Primary` | `LeatherL`: `PrimaryButton` style background, the "Cached catalog" heading and spinner in `StatusSheetPage.xaml` |
| Parchment (light) | `#FAF6EC` | Page background, primary-button text | `Background`, `PrimaryContrastText` | `PageBgL` (the `Page` style), `OnLeather` (text on leather buttons and on the Find banner) |
| Brass | `#A67B3A` | Secondary CTA bg, accent borders, active nav-link colour | `Secondary` | `Brass`: the Scan button fill and the search-field and sync-chip borders in `FindPage.xaml`; `BorderL` (card and outlined-button borders) |
| Ink | `#2C2416` | Primary text on parchment / on brass | `TextPrimary`, `SecondaryContrastText` | `TextL` (body text), `InkOnBrass` (Scan button text) |
| Espresso | `#3E2723` | Top app bar / mobile header banner | `AppbarBackground` | `HeaderL`: the Find banner (`FindPage.xaml`) and the Shell tab bar (`AppShell.xaml`) |
| Aged parchment | `#F2EADB` | Card / panel surface, drawer bg, espresso text colour | `Surface`, `BackgroundGray`, `AppbarText` | `SurfaceL` (the `Card` style background) |
| Faded ink | `#6B5D4A` | Secondary text, drawer icon, status / hint labels | `TextSecondary`, `DrawerIcon`, `ActionDefault` | `TextMutedL`: status and hint labels, `OutlinedButton` text |
| Green leather | `#3D5A3F` | Tertiary (rare); reserved for state colours / chart accents | `Tertiary` | `GreenL`: the series progress-bar fill in `SeriesGapsPage.xaml.cs` |

### Muted status colours

Deliberately muted so they sit inside the palette rather than shouting over it. From `BookTrackerTheme.cs:48–51`:

| Name | Hex | When |
|---|---|---|
| Success | `#4F6B3D` | Save confirmations, "Catalog loaded ✓"-style positive results |
| Info | `#3A6B7A` | Neutral informational messages, hint banners |
| Warning | `#B8861B` | Soft warnings — staleness banners, "consider re-syncing" prompts |
| Error | `#9B3B2E` | Validation errors, exception messages |

### Brand-mark colours (icon-only)

Each app's launcher icon is its own brand mark, deliberately distinct from the in-app palette so a phone with both apps installed reads them as different things at a glance.

**Bookcase PWA** uses Material You purple — a holdover from before the leather palette was settled. **These two values are intentionally outside the in-app palette** — they live only in the Web icon SVG and the PWA manifest `theme_color`.

| Name | Hex | Where |
|---|---|---|
| Brand purple | `#6750A4` | `BookTracker.Web/wwwroot/icons/icon.svg:2`, `wwwroot/manifest.webmanifest:10`, `Components/App.razor:15` (theme-color meta) |
| Brand purple dark | `#4F378B` | Same SVG, used for the book spine block |

**Bookshelf MAUI** uses the in-app palette (espresso background + brass spine + parchment text lines) so the launcher icon and running app read as one product. Distinct from the Bookcase brand purple so installing both apps doesn't put two identical purple icons in the launcher.

| Name | Hex | Where |
|---|---|---|
| Espresso (icon bg) | `#3E2723` | `BookTracker.Mobile/Resources/AppIcon/appicon.svg`, `BookTracker.Mobile/BookTracker.Mobile.csproj` (MauiIcon + MauiSplashScreen `Color`) |
| Brass (icon spine) | `#A67B3A` | `BookTracker.Mobile/Resources/AppIcon/appiconfg.svg`, `BookTracker.Mobile/Resources/Splash/splash.svg` |
| Parchment (icon text lines) | `#F2EADB` | Same SVGs as brass row |

See [Known inconsistencies](#known-inconsistencies) for the Bookcase-side resolution still pending.

### Surface accent colours (Web-only, transitional)

Bootstrap-era values still present in `BookTracker.Web/wwwroot/css/site.css` and `Components/Pages/Home.razor`. These are not part of the canonical palette — they're transitional and should be replaced with theme tokens during the `convert-as-we-touch` migration.

| Hex | Where | Replacement |
|---|---|---|
| `#fbf5ec → #f1e5d0` (gradient) | `site.css:57` `.hero` | Considered "parchment with warmth"; OK to keep until hero gets MudBlazor treatment |
| `#ead9be` | `site.css:58` `.hero` border | Soft brass; OK; subset of palette intent |
| `#e6e6e6` | `site.css:66` `.stat-card` border | Replace with `divider` token equivalent when stat tiles get MudBlazor refactor |
| `#e9ecef` | `site.css:77, 92` mobile card border-bottom | Bootstrap default; replace with theme `Divider` |
| `#f8f9fa` | `site.css:87` `:active` state | Bootstrap default; replace with theme `BackgroundGray` |
| `#258cfb` | `site.css:35` focus ring | Browser-blue default; theme replacement deferred — `:focus-visible` should land with the a11y review (TODO #19) |

## Typography

### Font stack

Roboto with sans-serif fallbacks. From `BookTrackerTheme.cs:55–58`:

```csharp
FontFamily = new[] { "Roboto", "Helvetica", "Arial", "sans-serif" }
```

Mobile loads OpenSans (`OpenSans-Regular`, `OpenSans-Semibold`) and the Tabler icon font in `MauiProgram.cs`; the implicit styles in `Resources/Styles/Styles.xaml` set `OpenSansRegular` as the text font, so Mobile text is OpenSans rather than Web's Roboto.

### Size scale

Web uses MudBlazor's built-in typography variants (`MudText Typo="@Typo.h4"`, etc.) plus a responsive base size:

```css
/* site.css:1-9 */
html { font-size: 14px; }
@media (min-width: 768px) {
    html { font-size: 16px; }
}
```

So all `rem`-based MudBlazor sizes scale 14:16 between mobile and tablet+.

Mobile uses explicit `FontSize` values on each Label / Button, in XAML and in the page code-behind that builds rows. Recurring scale (sampled across `BookTracker.Mobile/Pages/` and `Resources/Styles/Styles.xaml`):

| Size | Use | Examples |
|---|---|---|
| 24 | Page title | `FindPage.xaml:35` (Find banner), `StatusSheetPage.xaml:17` |
| 18 | Result-card title | `ResultPage.xaml:60` |
| 16 | Standard button text | `PrimaryButton` style (`Styles.xaml:461`) |
| 15 | Panel content text | `StatusSheetPage.xaml:39` (cache stats line), `ResultPage.xaml:31` (wishlist flag) |
| 14 | Body labels, outlined buttons | `OutlinedButton` style (`Styles.xaml:474`), `ResultPage.xaml:62` (authors), `FindPage.xaml:76` (Scan) |
| 13 | Secondary and status text | `StatusSheetPage.xaml:53`, `ResultPage.xaml:64`, the scope buttons at `FindPage.xaml:115–117` |
| 12 | Microcopy (timestamps, section labels, ISBN in Courier New) | `StatusSheetPage.xaml:35, 42`, `ResultPage.xaml:66`, the sync chip at `FindPage.xaml:47` |
| 11 | Footnotes | `StatusSheetPage.xaml:71` (build footer) |

When adding a new size, snap to the nearest existing rung rather than introducing intermediate values.

### Weight

- **Bold** for: page titles, primary CTA button text, panel section headings, found-state book titles.
- **Regular** for: secondary buttons, body text, hints, subtitles.

No semibold or other weights are in use; introducing one would need a paired decision on where else it applies.

## Spacing & sizing

Recurring values across both surfaces. Use these rungs unless there's a specific reason not to.

### Padding / margin / spacing scale (dp on Mobile, px on Web)

| Value | Use |
|---|---|
| 4 | Tight vertical spacing inside a label group (`StatusSheetPage.xaml:33`, timestamp under stats) |
| 6 | Inner spacing inside result cards (`ResultPage.xaml:59`) |
| 8 | CornerRadius on Borders / buttons; small element gaps (`FindPage.xaml:53`) |
| 12 | Standard component padding (the `Card` style, `Styles.xaml:486`; `ResultPage.xaml:42`); standard element spacing in stacks (`ResultPage.xaml:14`) |
| 14 | Horizontal padding of code-built rows (`FindPage.xaml.cs:272`, `SeriesGapsPage.xaml.cs:151`) |
| 16 | Spacing between sections of a sheet (`StatusSheetPage.xaml:13`); top padding under a header (`FindPage.xaml:53`, `WishlistPage.xaml:26`) |
| 20 | Page content padding (`StatusSheetPage.xaml:13`, `ResultPage.xaml:14`, `FindPage.xaml:120`) |
| 24 | Header banner horizontal padding (`FindPage.xaml:32`) |
| 32 | No current use on Mobile |

### Touch / click target heights (Mobile)

| Size | Use |
|---|---|
| 44 dp | Low-emphasis outlined buttons (`OutlinedButton` style; Close, Sign in / Sign out on the status sheet) — sits at Material's minimum |
| 48 dp | The Find tab's Scan button (`FindPage.xaml:72`), sized to sit beside the search field |
| 56 dp | Standard primary action buttons (`PrimaryButton` style) |
| 64 dp | High-emphasis in-bookshop actions (`KillerButton` style) — the style exists in `Styles.xaml` but no page uses it |

Web targets default to MudBlazor's `Medium` size on `MudButton`; switch to `Large` for primary CTA on dense pages.

### Corner radius

8 dp/px on most cards, buttons, panels. Both surfaces. Rationale: distinctive enough to read as "rounded" without going round-everything Material 3. Splash icon background uses 80 px on a 512-canvas (`icon.svg:2`) — visually equivalent.

## Component patterns

Cross-surface implementations of the recurring UI shapes. Each pattern's reference impl is named so future drift can be checked against a single canonical example.

### Primary CTA button

The standard "do the main thing on this screen" button. Leather background, parchment text (bold on Web).

**Web** (MudBlazor):
```razor
<MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Large">
    Save
</MudButton>
```
Reference: the Save button in `Components/Shared/BookEditDialog.razor` (and the other edit dialogs under `Components/Shared/`).

**Mobile** (MAUI) — the `PrimaryButton` style (`Resources/Styles/Styles.xaml:457–464`: `LeatherL`/`LeatherD` background, `OnLeather` text, 16, 56 dp, corner radius 8):
```xml
<Button Text="Refresh now" Style="{StaticResource PrimaryButton}" />
```
References: `BookTracker.Mobile/Pages/StatusSheetPage.xaml:58–61` (Refresh now), `Pages/ResultPage.xaml:68–69` (Open in app).

### Brass-accent CTA (in-bookshop killer action)

A step up from primary — used sparingly for the action that defines the page. Brass background, ink text, larger height.

**Mobile**: the `KillerButton` style (`Styles.xaml:447–454`, 64 dp, bold 18) defines this treatment, but no page uses it. The Find tab's Scan button (`Pages/FindPage.xaml:68–78`) carries the brass fill and ink text inline at 48 dp beside the search field.

**Web equivalent** doesn't yet exist as a distinct treatment — the `/bookshop` scan action uses the same primary-button shape. When `/bookshop` next gets touched, consider whether the Scan button there should pop the same way.

### Card / panel (passive info)

Surface for showing state to the user — cached counts, scan results, status indicators. Aged-parchment surface, brass border, 8 corner radius.

**Web** (MudBlazor):
```razor
<MudPaper Class="pa-4" Elevation="0" Style="border: 1px solid #A67B3A; background: #F2EADB; border-radius: 8px;">
    <!-- content -->
</MudPaper>
```

**Mobile** (MAUI) — the `Card` style (`Resources/Styles/Styles.xaml:480–488`: `SurfaceL`/`SurfaceD` background, `BorderL`/`BorderD` 1 px stroke, padding 12, corner radius 8):
```xml
<Border Style="{StaticResource Card}">
    <!-- content -->
</Border>
```
References: `Pages/StatusSheetPage.xaml:32–45` (cached-catalog panel). The result cards in `Pages/ResultPage.xaml` keep the same shape with their own tokens: `FoundFrame` (lines 38–85, below) and `MissingFrame` (lines 88–103, the neutral `MissBg` / `MissBorder` variant).

### Result card — found state

Same shape as the generic panel above, but tints to communicate success:
- Border: `#A3D9A5` light / `#356735` dark
- Background: `#E8F5E9` light / `#1B3320` dark

These are the `FoundBorderL`/`FoundBorderD` and `FoundBgL`/`FoundBgD` tokens. Reference: `BookTracker.Mobile/Pages/ResultPage.xaml:38–85`. The MAUI version uses `AppThemeBinding` to switch between light + dark; Web is light-mode only.

### Outlined low-emphasis button

For actions that should be available but not invitational (Sign out, dismissive Cancel, "open advanced settings").

**Mobile** — the `OutlinedButton` style (`Resources/Styles/Styles.xaml:467–477`: transparent, `BorderL`/`BorderD` 1 px border, `TextMutedL`/`TextMutedD` text, 14, 44 dp, corner radius 8):
```xml
<Button Style="{StaticResource OutlinedButton}" HorizontalOptions="Center" WidthRequest="160" />
```
References: `Pages/StatusSheetPage.xaml:63–68` (Sign in / Sign out), `StatusSheetPage.xaml:21–25` (Close).

**Web**: `<MudButton Variant="Variant.Outlined" Color="Color.Secondary">`.

### Header banner (Mobile Find tab)

In-content header that replaces the Shell navigation bar. Espresso (`HeaderL`/`HeaderD`) background, parchment (`OnLeather`) title, and a brass-bordered sync chip on the right.

Reference: `Pages/FindPage.xaml:29–50`. The page sets `Shell.NavBarIsVisible="False"` to hide the Shell bar, then renders this banner as the topmost grid row. Find is the only page with a banner.

The other pages (Wishlist and Gaps tabs, `ResultPage`, `AuthorWorksPage`) keep the Shell navigation bar with a title; on the drill-down pages it carries the back button. The status sheet is pushed modally (`PushModalAsync`) as a bare `ContentPage`, so it draws its own "Sync & account" heading and Close button instead of a navigation bar.

### Web nav bar (themed)

Bootstrap navbar with the leather/brass palette layered on via `.navbar-themed`. Structure stays Bootstrap (data-bs-toggle hamburger collapse at `lg`); visual is themed.

References: `Components/Layout/MainLayout.razor:24–73`, `wwwroot/css/site.css:158–181`.

## Layout patterns

### Find tab (Mobile)

```
┌──────────────────────────────┐
│  [espresso banner] [sync chip]│  ← row 0, Auto
├──────────────────────────────┤
│  [search field]  [Scan]      │  ← row 1, Auto
│  [inline camera, collapsed]  │  ← row 2, Height 0 ↔ 220 in code
│  [scope segment, if results] │  ← row 3, Auto
│  [results scrollview]        │  ← row 4, *
└──────────────────────────────┘
```

Reference: `Pages/FindPage.xaml:15–123`. `Shell.NavBarIsVisible="False"` hides the Shell bar at the very top. The Shell bottom tab bar (`AppShell.xaml`) sits below every tab.

### Other pages (Mobile)

Shell navigation bar (title, plus a back arrow on drill-downs) + `ContentPage` styled with `Page`. No in-content banner. References: `Pages/WishlistPage.xaml`, `Pages/SeriesGapsPage.xaml`, `Pages/ResultPage.xaml`, `Pages/AuthorWorksPage.xaml`.

### Web page

Standard Bootstrap container layout via `MainLayout.razor`:

```
┌───────────────────────────────────────────────────┐
│  [.navbar-themed espresso navbar, container-bound]│
├───────────────────────────────────────────────────┤
│  [.container main content]                        │
│                                                   │
├───────────────────────────────────────────────────┤
│  [.footer with copyright + version SHA]           │
└───────────────────────────────────────────────────┘
```

Reference: `Components/Layout/MainLayout.razor`. The version-SHA footer link is sourced from `BuildInfo.ShortSha` (injected at publish time via `dotnet publish /p:SourceRevisionId=...`).

### Scan UX (both surfaces)

Camera viewfinder + letterbox overlay + green-bordered scan target window in the centre. Aspect ratio approximately 3.5:1 (width:height) — book barcode shape.

**Web**: html5-qrcode library renders its own qrbox at 280×80 px. Reference: `BookTracker.Web/wwwroot/js/barcode-scanner.js:23`.

**Mobile**: an inline scanner on the Find tab, a hand-rolled XAML overlay layered above the ZXing CameraBarcodeReaderView in a 220 dp grid row that the Scan button expands. Star sizing (`1.5*:1*:1.5*` rows × `*:6*:*` cols) gives ~75% width × 25% height; no letterbox dimming (at the inline size it read as a grey box); the `ScanReticle` token (`#4CAF50`, Material green) as a 3 px stroke on the scan target. Reference: `BookTracker.Mobile/Pages/FindPage.xaml:81–106`.

Note: the Mobile scan target uses Material green rather than the leather palette because "scan UI" is a recognised cross-app convention (green = recognised barcode area). Diverging from convention for brand consistency would be net-negative.

## Iconography

### Brand marks

Each app has its own launcher icon — same spine + text-lines design, different palette.

**Bookcase PWA:** `BookTracker.Web/wwwroot/icons/icon.svg`. 512×512 viewBox; rounded purple rectangle background with a darker book-spine block on the left and three white text-line stripes representing the pages.

**Bookshelf MAUI:** `BookTracker.Mobile/Resources/AppIcon/appicon.svg` (background) + `appiconfg.svg` (foreground). Espresso `#3E2723` background, brass `#A67B3A` spine, parchment `#F2EADB` text lines — the in-app palette, so the launcher icon and running app are visually continuous. Different from Bookcase's purple so a phone with both apps installed shows two distinct icons in the launcher.

Derivatives:

| File | Purpose |
|---|---|
| `BookTracker.Web/wwwroot/icons/icon-192.png` | PWA manifest 192×192 (Android home-screen install) |
| `BookTracker.Web/wwwroot/icons/icon-512.png` | PWA manifest 512×512 (Android splash, taskbar high-DPI) |
| `BookTracker.Web/wwwroot/icons/apple-touch-icon.png` | iOS Safari "Add to home screen" |
| `BookTracker.Mobile/Resources/AppIcon/appicon.svg` | MAUI Android adaptive-icon **background** layer — solid espresso `#3E2723` (the in-app header banner colour). Differentiates from Bookcase's purple. |
| `BookTracker.Mobile/Resources/AppIcon/appiconfg.svg` | MAUI Android adaptive-icon **foreground** layer — brass `#A67B3A` spine + parchment `#F2EADB` text lines, scaled to 58% and centred in the inner 300×300 safe zone of the 456×456 canvas |
| `BookTracker.Mobile/Resources/Splash/splash.svg` | MAUI splash screen — same spine + lines design, full-bleed; MauiSplashScreen Color is set to `#3E2723` so the surrounding fill matches the icon background |

PNG renders are generated reproducibly by `scripts/generate-pwa-icons.ps1`.

### Android adaptive icon safe zone

When designing or updating the Mobile launcher foreground SVG, the visible design must live within the inner **66%** of the canvas (≈ a 300×300 region centred on the 456×456 foreground SVG). Anything outside that radius will be cropped by round / squircle / teardrop launcher masks. The current foreground uses a nested viewBox to map the original 0–512 PWA design into the inner 300×300 region — preserves the design shape and respects the safe zone.

### In-app glyphs

Web uses MudBlazor's `MudIcon` with Material icons (`Icons.Material.Filled.Search`, etc.) where icons are needed inline. Don't mix Material font icons with emoji on the same surface — pick one per page.

Mobile's target is the Tabler outline icon font (registered in `MauiProgram.cs`, glyphs in `Theming/TablerIcons.cs`), not emoji (see `docs/BOOKSHELF-UI-REDESIGN.md` §3). The code does not meet that yet: Tabler supplies only the three tab icons in `AppShell.xaml.cs`, and the pages use emoji and symbol characters — the `📖` cover placeholder, `⭐ On your wishlist` in `ResultPage.xaml`, and `⟳` / `⚠ offline` on the Find sync chip. Moving them to Tabler is tracked in the backlog.

## Accessibility

### Touch targets

- Mobile: 44 dp minimum per Material guidelines; 48 dp for the Find tab's Scan button; 56 dp for standard primary actions; 64 dp for in-bookshop killer actions (the `KillerButton` style, which no page uses).
- Web: MudBlazor's `Size="Size.Large"` ≈ 48 px height; matches the touch-target floor when the page is used on a phone.

### Contrast (light theme)

Approximate WCAG contrast ratios for the canonical pairings (calculated against the canonical hex values):

| Foreground | Background | Ratio | Level |
|---|---|---|---|
| Parchment `#FAF6EC` | Leather `#6B2737` | ≈ 9.4:1 | AAA |
| Parchment `#FAF6EC` | Espresso `#3E2723` | ≈ 13.4:1 | AAA |
| Ink `#2C2416` | Parchment `#FAF6EC` | ≈ 13.3:1 | AAA |
| Ink `#2C2416` | Brass `#A67B3A` | ≈ 4.7:1 | AA (large + UI) |
| Ink `#2C2416` | Aged parchment `#F2EADB` | ≈ 12.4:1 | AAA |
| Faded ink `#6B5D4A` | Parchment `#FAF6EC` | ≈ 4.9:1 | AA |
| Brass `#A67B3A` | Parchment `#FAF6EC` | ≈ 3.5:1 | Fails for body text (AA needs 4.5); OK for non-text accents only |

Implication: never set body text in brass on parchment — brass is for accents, borders, and ink-on-brass button text. Active nav link uses brass on espresso (the navbar's dark surface), where contrast is fine.

### Focus & keyboard navigation

Web has a Bootstrap-default focus ring at `site.css:34–36` (white + blue glow). A theme-coloured replacement is deferred — pairs with the broader a11y review (TODO #19). Mobile keyboard / D-pad navigation is not a priority for v1 (no hardware keyboard expected on the target device); MAUI's defaults apply.

### Screen reader semantics

Web inherits Bootstrap / MudBlazor semantics by default. The `.clickable-tile` cards on Home use `role="link"` for assistive tech (`site.css:127`). Mobile leans on MAUI's accessibility properties — `SemanticProperties.Description` should be set on icon-only buttons; not yet enforced systematically.

## Platform-specific quirks

### Web

- **MudBlazor + Bootstrap coexist by design.** "Convert as we touch" — each page converts to MudBlazor when it's substantively edited for other reasons. No big-bang rewrite. See `ARCHITECTURE.md` > UI component library for the migration status.
- **Theme provider lives at the root layout.** `MainLayout.razor:11` injects `<MudThemeProvider Theme="BookTrackerTheme.Default" />`. The other MudBlazor providers (`Popover`, `Dialog`, `Snackbar`) are also at the root so any page can dispatch them.
- **PWA installable + offline-capable.** Manifest at `wwwroot/manifest.webmanifest`, service worker at `wwwroot/service-worker.js`. The `theme_color` is brand purple (`#6750A4`), not leather — see [Known inconsistencies](#known-inconsistencies).
- **Custom CSS lives in `site.css`.** Layered on top of MudBlazor + Bootstrap. New ad-hoc CSS should justify why MudBlazor's theme tokens can't carry the change first.
- **Responsive breakpoints.** Bootstrap defaults (`sm` 576, `md` 768, `lg` 992, `xl` 1200). The font-size step at 768 px (`site.css:1–9`) is the only project-specific one.

### Mobile (MAUI Android)

- **Android-only.** iOS deferred until / unless an Apple developer account materialises; the design is platform-agnostic enough to add `net10.0-ios` later without restructuring. See `BookTracker.Mobile.csproj:1–8`.
- **Design tokens, not a theme provider.** MAUI has no equivalent of `MudThemeProvider`. Bookshelf uses a token set (listed in `docs/BOOKSHELF-UI-REDESIGN.md` §2) whose light/dark pairs resolve via `AppThemeBinding`; `Theming/ThemeColors` bridges them into code. Values are mirrored manually from `BookTrackerTheme.cs` — see [Known inconsistencies](#known-inconsistencies).
- **Shell + bottom TabBar.** Find / Wishlist / Gaps tabs; drill-downs push within the active tab's stack. See `docs/BOOKSHELF-UI-REDESIGN.md` §1.
- **Adaptive icon safe zone.** Foreground SVG design must live in the inner 66% (≈ 300×300 of a 456×456 canvas) to survive launcher masks. See [Iconography](#iconography).

## Known inconsistencies

The honest version: not everything is consistent yet. These are the gaps the doc knows about so they don't get re-discovered.

### Brand-mark purple vs in-app leather palette

The Bookcase PWA launcher icon and `theme_color` use Material You purple (`#6750A4` / `#4F378B`) — a holdover from before the leather/brass palette was decided for the in-app UI. Tap the purple icon, land on a parchment-and-leather screen: not visually continuous.

**Bookshelf MAUI uses the in-app palette** for its launcher icon (espresso bg + brass spine + parchment text lines), so two purple icons don't sit side by side in the launcher. Bookshelf is consistent end-to-end; Bookcase has the purple gap.

Options for the Bookcase resolution (deferred, TODO #38):

1. **Rebrand the Bookcase icon** to the leather palette (matching Bookshelf's design or its own variant). Touches `BookTracker.Web/wwwroot/icons/icon.svg`, the PNG renders via the regen script, `wwwroot/manifest.webmanifest` `theme_color`, `Components/App.razor` `theme-color` meta.
2. **Adopt the icon palette in-app** — invert the choice: use the purple as the actual brand identity, retheme the in-app palette. Wider blast radius than (1) and would walk back Bookshelf's alignment.
3. **Accept and document** — current state. The purple is brand-mark only; the parchment-and-leather is the product palette. Many apps do this (e.g. Slack's neon icon vs muted desktop chrome).

(3) is the current state. Revisit if the inconsistency causes confusion in real use.

### Mobile palette is manually mirrored from Web

Bookshelf's token dictionary holds hex values copied from `BookTrackerTheme.cs`. The canonical source is `BookTrackerTheme.cs`, but there's no language-level enforcement that Mobile stays in sync, and the drift risk grows over time.

Long-term resolution options:

1. **Shared palette file** — generate both `BookTrackerTheme.cs` and a MAUI ResourceDictionary from a single source-of-truth file (YAML / JSON / etc.). Real fix; over-engineered for one colour set.
2. **Status quo + style-audit skill** (recommended) — keep manual mirroring, build a style-audit skill (TODO #36) that diffs the `Colors.xaml` token values against `BookTrackerTheme.cs` and flags drift as a finding.

### MudBlazor migration in flight

Some Web pages are still Bootstrap-shaped; some are MudBlazor; some are hybrids. The "convert as we touch" rule means this state persists indefinitely. The Bootstrap-era CSS in `site.css:56–94` (hero gradient, stat cards, mobile card classes, scanner container) is the canary — each block migrates when its host page next gets a substantive edit.

### Web's `/bookshop` scan UX vs Mobile's

The Web `/bookshop` scan page uses html5-qrcode's default qrbox styling — green target inside a dark letterbox, all rendered by the library. Mobile's scanner hand-rolls an equivalent overlay in XAML to match the same visual shape. Both look approximately the same on device, but they're independently maintained — a styling change to one won't automatically apply to the other. Likely acceptable: the two implementations are short and the visual contract is documented above.
