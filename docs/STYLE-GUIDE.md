# BookTracker style guide

Visual and interaction conventions covering both surfaces of the product:

- **Web** — `BookTracker.Web` (Blazor Web App, MudBlazor + Bootstrap holdouts during the convert-as-we-touch migration).
- **Mobile** — `BookTracker.Mobile` (.NET MAUI Android companion).

This document **describes** what's already in the code. The canonical source of truth for theme values is `BookTracker.Web/Theme/BookTrackerTheme.cs` for Web and the inline hex literals in MAUI XAML for Mobile (which mirror the Web values manually — see [Known inconsistencies](#known-inconsistencies) for the long-term plan to close that gap). When something here disagrees with the code, the code wins; the doc was the one that got stale.

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

What this guide is **not**: an exhaustive design system. There's no published component library or token registry yet — BookTracker is one developer's personal project at the time of writing. The conventions captured here are the ones that exist *de facto* in the code; the guide makes them explicit so they don't drift the next time something is touched without the other side getting the same treatment.

## Palette

The library palette — leather spines, brass fixtures, parchment pages, espresso ink. Mirrored from `BookTracker.Web/Theme/BookTrackerTheme.cs:11–52`.

| Name | Hex | Semantic role | Web token | Mobile usage |
|---|---|---|---|---|
| Leather | `#6B2737` | Primary actions, primary text accent (e.g. panel headings) | `Primary` | `MainPage.xaml:48` (Sign in btn bg), spinner colour |
| Parchment (light) | `#FAF6EC` | Page background, primary-button text | `Background`, `PrimaryContrastText` | `MainPage.xaml:7` (page bg), `#F2EADB` button text via theme contrast |
| Brass | `#A67B3A` | Secondary CTA bg, accent borders, active nav-link colour | `Secondary` | `MainPage.xaml:62` (Scan ISBN bg), `MainPage.xaml:90` (stats panel border) |
| Ink | `#2C2416` | Primary text on parchment / on brass | `TextPrimary`, `SecondaryContrastText` | `MainPage.xaml:62` (Scan text colour on brass bg) |
| Espresso | `#3E2723` | Top app bar / mobile header banner | `AppbarBackground` | `MainPage.xaml:25` (header banner bg) |
| Aged parchment | `#F2EADB` | Card / panel surface, drawer bg, espresso text colour | `Surface`, `BackgroundGray`, `AppbarText` | `MainPage.xaml:91` (stats panel bg) |
| Faded ink | `#6B5D4A` | Secondary text, drawer icon, status / hint labels | `TextSecondary`, `DrawerIcon`, `ActionDefault` | `MainPage.xaml:114` (StatusLabel), `MainPage.xaml:131` (Sign out text) |
| Green leather | `#3D5A3F` | Tertiary (rare); reserved for state colours / chart accents | `Tertiary` | Not yet used in Mobile |

### Muted status colours

Deliberately muted so they sit inside the palette rather than shouting over it. From `BookTrackerTheme.cs:48–51`:

| Name | Hex | When |
|---|---|---|
| Success | `#4F6B3D` | Save confirmations, "Catalog loaded ✓"-style positive results |
| Info | `#3A6B7A` | Neutral informational messages, hint banners |
| Warning | `#B8861B` | Soft warnings — staleness banners, "consider re-syncing" prompts |
| Error | `#9B3B2E` | Validation errors, exception messages |

### Brand-mark purple (icon-only)

The PWA / launcher icon uses Material You purple as a brand-mark holdover from before the leather palette was settled. **These two values are intentionally outside the in-app palette** — they live only in the icon SVGs and the PWA manifest `theme_color`. Listed here for completeness; do not use elsewhere.

| Name | Hex | Where |
|---|---|---|
| Brand purple | `#6750A4` | `BookTracker.Web/wwwroot/icons/icon.svg:2`, `BookTracker.Mobile/Resources/AppIcon/appicon.svg:7`, `wwwroot/manifest.webmanifest:10`, `Components/App.razor:15` (theme-color meta) |
| Brand purple dark | `#4F378B` | Same SVGs, used for the book spine block |

See [Known inconsistencies](#known-inconsistencies) for the long-term plan.

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

Mobile inherits the Android system font by default (Roboto on most devices, the OEM equivalent otherwise). No custom font is loaded on Mobile — the OS rendering of Roboto matches Web within tolerance.

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

Mobile uses explicit `FontSize` values on each Label / Button. Recurring scale (sampled across `MainPage.xaml`, `ScanPage.xaml`):

| Size | Use | Examples |
|---|---|---|
| 24 | Page title in header banner | `MainPage.xaml:30` |
| 18 | Primary CTA button text | `MainPage.xaml:65` (Scan ISBN) |
| 16 | Standard button text | `MainPage.xaml:53` (Sign in / Load catalog) |
| 15 | Panel content text | `MainPage.xaml:108` (cache stats line) |
| 14 | Status / body labels, outlined buttons | `MainPage.xaml:120, 137` |
| 13 | Subtitle, hint text | `MainPage.xaml:33` (header subtitle) |
| 12 | Microcopy (timestamps, footnotes, "Cached catalog" panel label) | `MainPage.xaml:103, 111` |

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
| 4 | Tight vertical spacing inside a label group (e.g. timestamp under stats) |
| 6 | Inner spacing inside result cards (`ScanPage.xaml:46`) |
| 8 | CornerRadius on Borders / buttons; small element gaps |
| 12 | Standard component padding (`ScanPage.xaml:42`); standard element spacing in stacks |
| 14 | Vertical spacing between primary actions (`MainPage.xaml:23`) |
| 16 | Panel inner padding (`MainPage.xaml:92`) |
| 20 | Page content padding (`MainPage.xaml:23`, ScrollView padding) |
| 24 | Header banner padding (`MainPage.xaml:27`); top spacing for fresh action groups |
| 32 | Separator-spacing above low-emphasis actions (`MainPage.xaml:138`, Sign out top margin) |

### Touch / click target heights (Mobile)

| Size | Use |
|---|---|
| 44 dp | Low-emphasis outlined buttons (Sign out) — sits at Material's minimum |
| 56 dp | Standard primary action buttons |
| 64 dp | High-emphasis in-bookshop actions (Scan ISBN) — slightly above the rest of the action stack so it stands out as the killer feature |

Web targets default to MudBlazor's `Medium` size on `MudButton`; switch to `Large` for primary CTA on dense pages.

### Corner radius

8 dp/px on most cards, buttons, panels. Both surfaces. Rationale: distinctive enough to read as "rounded" without going round-everything Material 3. Splash icon background uses 80 px on a 512-canvas (`icon.svg:2`) — visually equivalent.

## Component patterns

Cross-surface implementations of the recurring UI shapes. Each pattern's reference impl is named so future drift can be checked against a single canonical example.

### Primary CTA button

The standard "do the main thing on this screen" button. Leather background, parchment text, bold.

**Web** (MudBlazor):
```razor
<MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Large">
    Save
</MudButton>
```
Reference: `Components/Pages/Books/Edit.razor` (multiple sites).

**Mobile** (MAUI):
```xml
<Button Text="Sign in"
        HeightRequest="56"
        BackgroundColor="#6B2737"
        TextColor="#FAF6EC"
        FontSize="16"
        FontAttributes="Bold"
        CornerRadius="8"
        HorizontalOptions="Fill" />
```
Reference: `BookTracker.Mobile/MainPage.xaml:46–56`.

### Brass-accent CTA (in-bookshop killer action)

A step up from primary — used sparingly for the action that defines the page. Brass background, ink text, larger height.

**Mobile**: Scan ISBN on the home screen. `MainPage.xaml:60–69`.

**Web equivalent** doesn't yet exist as a distinct treatment — the `/bookshop` scan action uses the same primary-button shape. When `/bookshop` next gets touched, consider whether the Scan button there should pop the same way.

### Card / panel (passive info)

Surface for showing state to the user — cached counts, scan results, status indicators. Aged-parchment surface, brass border, 8 corner radius.

**Web** (MudBlazor):
```razor
<MudPaper Class="pa-4" Elevation="0" Style="border: 1px solid #A67B3A; background: #F2EADB; border-radius: 8px;">
    <!-- content -->
</MudPaper>
```

**Mobile** (MAUI):
```xml
<Border Stroke="#A67B3A"
        StrokeThickness="1"
        BackgroundColor="#F2EADB"
        Padding="16,12">
    <Border.StrokeShape>
        <RoundRectangle CornerRadius="8" />
    </Border.StrokeShape>
    <!-- content -->
</Border>
```
References: `MainPage.xaml:88–106` (cache stats panel), `ScanPage.xaml:37–53` (FoundFrame), `ScanPage.xaml:56–70` (MissingFrame — neutral variant with light-grey instead of brass).

### Result card — found state

Same shape as the generic panel above, but tints to communicate success:
- Border: `#A3D9A5` light / `#356735` dark
- Background: `#E8F5E9` light / `#1B3320` dark

References: `BookTracker.Mobile/Pages/ScanPage.xaml:37–53`. The MAUI version uses `AppThemeBinding` to switch between light + dark; Web is light-mode-only at the time of writing.

### Outlined low-emphasis button

For actions that should be available but not invitational (Sign out, dismissive Cancel, "open advanced settings").

**Mobile**:
```xml
<Button Text="Sign out"
        HeightRequest="44"
        BackgroundColor="Transparent"
        TextColor="#6B5D4A"
        BorderColor="#A67B3A"
        BorderWidth="1"
        FontSize="14"
        CornerRadius="8"
        HorizontalOptions="Center"
        WidthRequest="160" />
```
Reference: `MainPage.xaml:124–138`.

**Web**: `<MudButton Variant="Variant.Outlined" Color="Color.Secondary">`.

### Header banner (Mobile root pages)

In-content header that replaces the system nav bar on root pages where there's nothing to navigate back from. Espresso bg, parchment-text title + brass-text subtitle.

Reference: `MainPage.xaml:24–35`. The page sets `NavigationPage.HasNavigationBar="False"` to hide the system bar, then renders this banner as the topmost grid row.

Child pages (e.g. `ScanPage`) keep the system nav bar because the back button is important; they don't render their own banner.

### Web nav bar (themed)

Bootstrap navbar with the leather/brass palette layered on via `.navbar-themed`. Structure stays Bootstrap (data-bs-toggle hamburger collapse at `lg`); visual is themed.

References: `Components/Layout/MainLayout.razor:24–73`, `wwwroot/css/site.css:158–181`.

## Layout patterns

### Root page (Mobile)

```
┌──────────────────────────────┐
│  [espresso banner]           │  ← Grid.Row="0", Auto height
│  BookTracker                 │
│  Offline-capable companion   │
├──────────────────────────────┤
│  [parchment scrollview]      │  ← Grid.Row="1", *
│    [primary CTA buttons]     │
│    [stats panel]             │
│    [status label]            │
│    [outlined low-emph btn]   │
└──────────────────────────────┘
```

Reference: `MainPage.xaml:18–141`. `NavigationPage.HasNavigationBar="False"` hides the system bar at the very top.

### Child page (Mobile)

System nav bar (Title="…" + back arrow) + ContentPage. No in-content banner. Reference: `Pages/ScanPage.xaml`.

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

**Mobile**: hand-rolled XAML overlay layered above the ZXing CameraBarcodeReaderView. Star sizing (`1.5*:1*:1.5*` rows × `*:6*:*` cols) gives ~75% width × 25% height; `#B3000000` (70% opacity black) letterbox; `#4CAF50` Material-green 3px stroke on the scan target. Reference: `BookTracker.Mobile/Pages/ScanPage.xaml:20–58`.

Note: the Mobile scan target uses Material green rather than the leather palette because "scan UI" is a recognised cross-app convention (green = recognised barcode area). Diverging from convention for brand consistency would be net-negative.

## Iconography

### Brand mark (PWA + Mobile launcher)

Source: `BookTracker.Web/wwwroot/icons/icon.svg`. 512×512 viewBox; rounded purple rectangle background with a darker book-spine block on the left and three white text-line stripes representing the pages.

Derivatives:

| File | Purpose |
|---|---|
| `BookTracker.Web/wwwroot/icons/icon-192.png` | PWA manifest 192×192 (Android home-screen install) |
| `BookTracker.Web/wwwroot/icons/icon-512.png` | PWA manifest 512×512 (Android splash, taskbar high-DPI) |
| `BookTracker.Web/wwwroot/icons/apple-touch-icon.png` | iOS Safari "Add to home screen" |
| `BookTracker.Mobile/Resources/AppIcon/appicon.svg` | MAUI Android adaptive-icon **background** layer — solid `#6750A4` matching the PWA |
| `BookTracker.Mobile/Resources/AppIcon/appiconfg.svg` | MAUI Android adaptive-icon **foreground** layer — spine + text lines, scaled to 58% and centred in the inner 300×300 safe zone of the 456×456 canvas |
| `BookTracker.Mobile/Resources/Splash/splash.svg` | MAUI splash screen — same spine + lines design, full-bleed; MauiSplashScreen Color is set to `#6750A4` so the surrounding fill matches the icon background |

PNG renders are generated reproducibly by `scripts/generate-pwa-icons.ps1`.

### Android adaptive icon safe zone

When designing or updating the Mobile launcher foreground SVG, the visible design must live within the inner **66%** of the canvas (≈ a 300×300 region centred on the 456×456 foreground SVG). Anything outside that radius will be cropped by round / squircle / teardrop launcher masks. The current foreground uses a nested viewBox to map the original 0–512 PWA design into the inner 300×300 region — preserves the design shape and respects the safe zone.

### In-app glyphs

Mobile uses emoji glyphs inline in button text where a glyph aids recognition (📚 for catalog, 📷 for camera). Web uses MudBlazor's `MudIcon` with Material icons (`Icons.Material.Filled.Search`, etc.) where icons are needed inline. Don't mix Material font icons with emoji on the same surface — pick one per page.

## Accessibility

### Touch targets

- Mobile: 44 dp minimum per Material guidelines; 56 dp for standard primary actions; 64 dp for in-bookshop killer actions (Scan).
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

- **Android-only for v1.** iOS deferred until / unless an Apple developer account materialises; the design is platform-agnostic enough to add `net10.0-ios` later without restructuring. See `BookTracker.Mobile.csproj:1–8`.
- **No theme provider.** MAUI doesn't have an equivalent of `MudThemeProvider` for hex-token resolution at the time of writing. Palette values are hex literals inline in XAML, mirrored manually from `BookTrackerTheme.cs`. See [Known inconsistencies](#known-inconsistencies) for the long-term plan.
- **System nav bar is per-page.** `NavigationPage.HasNavigationBar="False"` on root pages (MainPage) where there's nothing to navigate from; left default-true on child pages (ScanPage) so the back button is visible.
- **`AppThemeBinding` for light/dark variants.** Used on result-card colours (`ScanPage.xaml:40–41`) — when the system theme is dark, the card swaps to a darker green pair. Not yet applied to the leather/brass palette generally; would be the v1.1 work.
- **Adaptive icon safe zone.** Foreground SVG design must live in the inner 66% (≈ 300×300 of a 456×456 canvas) to survive launcher masks. See [Iconography](#iconography).
- **Page-size native lib warnings (XA0141).** Upstream `xamarin.androidx.camera.core` and `SQLitePCLRaw.lib.e_sqlite3.android` packages haven't shipped 16 KB-page-aligned native libs yet. Documented as forward-compat noise; can't be fixed at our level.

## Known inconsistencies

The honest version: not everything is consistent yet. These are the gaps the doc knows about so they don't get re-discovered.

### Brand-mark purple vs in-app leather palette

The launcher icon and PWA `theme_color` use Material You purple (`#6750A4` / `#4F378B`) — a holdover from before the leather/brass palette was decided for the in-app UI. The icon-to-app transition is therefore not visually continuous: tap the purple icon, land on a parchment-and-leather screen.

Options for resolution (deferred):

1. **Rebrand the icon** to the leather palette (leather spine block + parchment lines + brass border). Most invasive — touches `icon.svg`, the PNG renders via the regen script, MAUI's appicon SVGs, MAUI splash, manifest `theme_color`, App.razor `theme-color` meta.
2. **Adopt the icon palette in-app** — invert the choice: use the purple as the actual brand identity, retheme the in-app palette. Wider blast radius than (1).
3. **Accept and document** — current state. The purple is brand-mark only; the parchment-and-leather is the product palette. Many apps do this (e.g. Slack's neon icon vs muted desktop chrome).

(3) is the current state. Revisit if the inconsistency causes confusion in real use.

### Mobile palette is manually mirrored from Web

`BookTracker.Mobile/MainPage.xaml` and `Pages/ScanPage.xaml` hard-code hex literals. The canonical source is `BookTrackerTheme.cs`, but there's no language-level enforcement that Mobile stays in sync. The two surfaces have drifted exactly zero times so far (because the gap is small and the Mobile arc is recent), but the risk increases over time.

Long-term resolution options:

1. **`Resources/Styles/Colors.xaml` ResourceDictionary on Mobile** — declare each colour as a `<Color x:Key="Leather">#6B2737</Color>` entry, reference via `{StaticResource Leather}`. Same drift risk against Web; just moves the single source within Mobile.
2. **Shared palette file** — generate both `BookTrackerTheme.cs` and a MAUI ResourceDictionary from a single source-of-truth file (YAML / JSON / etc.). Real fix; over-engineered for one colour set.
3. **Status quo + style-audit skill** (recommended) — keep manual mirroring, build a future style-audit skill (see new TODO row) that diffs Mobile XAML hex literals against `BookTrackerTheme.cs` values and flags drift as a finding.

### MudBlazor migration in flight

Some Web pages are still Bootstrap-shaped; some are MudBlazor; some are hybrids. The "convert as we touch" rule means this state persists indefinitely. The Bootstrap-era CSS in `site.css:56–94` (hero gradient, stat cards, mobile card classes, scanner container) is the canary — each block migrates when its host page next gets a substantive edit.

### Web's `/bookshop` scan UX vs Mobile's

The Web `/bookshop` scan page uses html5-qrcode's default qrbox styling — green target inside a dark letterbox, all rendered by the library. Mobile's ScanPage hand-rolls an equivalent overlay in XAML to match the same visual shape. Both look approximately the same on device, but they're independently maintained — a styling change to one won't automatically apply to the other. Likely acceptable: the two implementations are short and the visual contract is documented above.

### Mobile dark-mode coverage is partial

Result cards (`ScanPage.xaml:37–70`) use `AppThemeBinding` for light/dark variants. The home page palette is light-only (`MainPage.xaml`). Switching the device to dark mode currently renders the home page in its light palette, which clashes with the system chrome. A v1.1 pass to apply `AppThemeBinding` across the leather/brass values would close this gap.
