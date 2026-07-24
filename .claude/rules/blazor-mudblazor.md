---
paths:
  - "BookTracker.Web/**"
---

# Blazor / MudBlazor gotchas (BookTracker.Web)

Hard-won UI-layer lessons, auto-loaded when touching `BookTracker.Web/**`. Each section keeps its original memory name so `[[feedback_*]]` cross-references from retros still resolve by grep.

## feedback_dialog_vm_lifetime

When a MudBlazor dialog needs the page's initialised state (e.g. `VM.Book`), do NOT `@inject` the same VM type inside the dialog. BookTracker registers ViewModels as Transient, so `@inject` hands the dialog a brand-new instance whose state (Book, snapshot, etc.) is empty — the dialog calls VM methods that silently no-op or return empty.

**Why:** caught in PR 4 of the Add/View/Edit polish arc (2026-05-12). The "Add existing work" dialog @injected `BookDetailViewModel` and called `VM.SearchAttachableWorksAsync` — which short-circuits when `Book is null`. The full edit page worked because it has a different VM and didn't hit this code path. Drew's repro: "in the view page I get no works found for a work that the full edit page finds."

**How to apply:** when a dialog needs the page's VM state, follow the EditionCoverUploadDialog pattern — declare a `[Parameter, EditorRequired] public TheVM VM { get; set; }` on the dialog and pass it through from the page via `DialogParameters<TheDialog> { { x => x.VM, VM } }`. Dialogs that only need stateless services (IBookLookupService, IWorkSearchService, IDbContextFactory) can still `@inject` them — the rule is specifically about VMs that hold initialised page state.

## feedback_add_with_optional_existing

When designing an "add X" surface where X might already exist in the library (existing Work, existing Author, existing Series, …), do NOT split into two buttons ("Add new" / "Pick existing") or two stages ("first search, then if no match show the new form"). Use one entry point: a typeahead-y title input that surfaces matching existing rows as the user types, with free-text fall-through to capture-as-new.

**Why:** Drew's testing-feedback during the Add/View/Edit polish arc, 2026-05-13. Two buttons "faces the same problem as for Add — far better to be able to do a 'Add, but allow me to select an existing if one exists' pattern." The cost of always-search is zero keystrokes (debounced, server-side); the cost of guessing wrong on the toggle is a broken flow. Drew steered me to this twice in the same arc (Add Book collection rows in PR 5, View-page Add Work in PR 6) before I memorised the pattern. Shipped as `MudAutocomplete<WorkSearchResult>` + capture-phase JS suppression of auto-pick (see `feedback_mudautocomplete_capture_phase.md`).

**How to apply:** when planning a new "add an X to Y" surface, default to:
- Single input field (autocomplete shape).
- Suggestions appear from existing rows as the user types (≥2 chars typical, 250ms debounce).
- Down + Enter on a highlighted suggestion → attach existing (skips the create form).
- Free-text Enter / Tab away from suggestions → falls through to the create path (inline form fields, or "Save" button reveal).
- A confirm step only if the attach-vs-create distinction has destructive consequences (e.g. fat-finger flick on a toggle that discards in-flight attachments — see `Add.razor`'s collection toggle confirm).

If a stakeholder asks for "two buttons" or "a mode toggle," push back with this shape unless there's a concrete reason the discovery cost is real (e.g. the existing list is so dense that typeahead is noisy — rare).

## feedback_mudautocomplete_capture_phase

MudAutocomplete<T> (MudBlazor 9.4 verified — likely same in earlier 9.x) auto-highlights the first dropdown match as soon as the dropdown opens, and Enter commits the highlighted item via `ValueChanged`. There's no `AutoHighlight=false` / `SelectFirstByDefault=false` property — verified by searching the MudBlazor 9.4 XML doc. This trips the "Ask, but allow free-text" UX pattern (`feedback_add_with_optional_existing.md`): typing "Con" + Enter would commit "Condor" instead of capturing "Con" as new.

**Why:** Add/View/Edit polish arc, 2026-05-13. Drew's repro after PR 5 shipped. Surface: Add Book collection row title autocomplete; same shape ported to View-page Add Work dialog in PR 6.

**How to apply:** when you need MudAutocomplete to differentiate "user explicitly arrow-keyed to this suggestion" from "first match auto-highlighted," install a capture-phase keydown listener on `document` that:

1. Anchors on a `data-*` attribute on a wrapper `<div>` around the autocomplete (MudAutocomplete's `<input>` is deep inside its DOM; you can't put `data-*` on the input directly).
2. Tracks per-input navigation state via a `WeakSet`:
   - `ArrowDown` / `ArrowUp` → add to set (user is navigating).
   - Printable keys / `Backspace` / `Delete` / `Escape` → remove from set (user is typing or dismissed).
3. On `Enter`:
   - Always `e.preventDefault()` (stops form submit).
   - If in set → `return` (let MudAutocomplete's own listener, which fires at bubble phase on the input, commit the highlighted item via `ValueChanged`).
   - If NOT in set → `e.stopImmediatePropagation()` to block MudAutocomplete's auto-pick; then invoke the page's free-text handler via `DotNetObjectReference.invokeMethodAsync`.

Capture phase (`addEventListener(..., true)`) is load-bearing — at bubble phase the input's own listener has already fired. Capture lets us decide before MudAutocomplete sees the event.

Also set `CoerceText="false"` on the autocomplete so the typed text survives Esc / un-picked dropdown close (default `true` reverts Text to the Value's display string, which is `null` when nothing is committed).

**Known limitation accepted in the arc:** after a free-text Enter, MudAutocomplete's portaled popover stays open (the `stopImmediatePropagation` means MudAutocomplete never learns about the Enter, so it doesn't close its own popover). Three closing-attempts failed (`input.blur()`, synthetic Esc keydown, direct `.mud-popover-open` removal — all blocked by MudBlazor's event delegation filtering synthetic events / the portaled DOM not cascading focus). User dismisses with Esc. Inline JS comment notes the dead-end paths so the next reader doesn't re-walk them. Live with it unless MudBlazor exposes a `close()` method.

Reference implementation: `BookTracker.Web/wwwroot/js/collection-works.js` + the dialog/page consumers (`Components/Pages/Books/Add.razor`, `Components/Shared/AddWorkDialog.razor`).

## feedback_mudblazor_menu_popover


Inline row-action menus (e.g. the Library status quick-set) in MudBlazor v8/v9 have three traps that each silently produce a button that "registers the click but no menu appears". All three cost real iterations on the `/books` status menu (2026-06, MudBlazor 9.4.0):

1. **`MudButton` inside `<ActivatorContent>` stops click propagation by default**, so the click never reaches `MudMenu`'s toggle and the menu never opens. Fix: use MudMenu's **own** activator — `<MudMenu Label="..." Variant="..." Color="..." EndIcon="...">` with the items as direct child content. MudMenu then owns the button + its click. Do NOT reach for `ActivatorContent` + a custom button for a menu.
2. **A click-less `MudChip` as the activator swallows the click** too (no `OnClick` → it consumes rather than forwards). Same symptom.
3. **Wrapping the menu in `<div @onclick:stopPropagation="true">` breaks the popover** — MudMenu/MudSelect popovers need the click to reach the document to stay open; stopPropagation opens-then-instantly-dismisses. `MudRating` survives the same wrapper because its stars are the directly-clicked element (no popover) — so "rating works but menu doesn't" is the tell.

**Why:** the misleading precedent is `EditionCoverUploadDialog`, where a `MudButton` in `MudFileUpload`'s `ActivatorContent` works fine — but `MudFileUpload` wires its activator via a label/hidden-input, not click-bubbling, so it doesn't generalise to `MudMenu`.

**How to apply:** for a row-action menu use the `Label`/`Icon` activator form. To keep inline-editable controls from triggering a clickable row's navigation, **don't** use a stopPropagation wrapper — decouple instead: put navigation on explicit child elements (cover image only — keeps the title selectable for copy/paste) rather than a whole-row `OnRowClick`. Related: [[feedback_mudautocomplete_capture_phase]], [[feedback_dialog_vm_lifetime]].

## feedback_mudblazor_valuechanged_not_commit


A MudAutocomplete's `ValueChanged` is **not** a "user is finished" signal. With `CoerceValue="true"` it fires on **every keystroke** (the typed text is coerced into the value continuously). Wiring an eager side effect (a DB write, a dispatch) to `ValueChanged` therefore runs it per character — e.g. eager-creating a publisher on commit produced rows `"spits in t"`, `"spits in th"`, `"spits in the"`, … as Drew typed "spits in the eye" (TD-15a PR2, 2026-06-29).

**Why:** there is no detectable "done" point inside `ValueChanged`. Pausing to think or fixing a typo is indistinguishable from finishing. Free-text autocompletes (no chips) don't emit a discrete commit the way the chip picker does (Enter/comma via the JS capture layer — [[feedback_mudautocomplete_capture_phase]]).

**How to apply:** for any eager/persisting action behind a free-text autocomplete, bind the value with plain `@bind-Value` (typing just updates the string, no side effect) and trigger the action from an **explicit gesture** — `OnBlur` (field lost focus) is the robust one; Enter via a keydown handler if needed. On blur, read the now-settled bound value, then act. Keep it best-effort (the save-time find-or-create net still guarantees the row if blur is missed). General rule: **stop trying to be clever with MudBlazor change events — only act on a gesture that genuinely means "the user is finished"** (Drew, 2026-06-29). Same family as [[feedback_mudblazor_menu_popover]] and [[feedback_dialog_vm_lifetime]] — MudBlazor's event surface rarely matches the intuitive mental model.

Context: TD-15a eager-create arc ([[retro_td15a_eager_create_arc]]).

## feedback_blazor_virtualize_using

`<Virtualize>` lives in `Microsoft.AspNetCore.Components.Web.Virtualization`, which is **not** in this project's `_Imports.razor`. Every page that uses it must add `@using Microsoft.AspNetCore.Components.Web.Virtualization` itself (e.g. `Authors/Index.razor` does).

**Why this matters:** without the using, Razor doesn't resolve `<Virtualize>` to the component — it treats it as a **raw HTML element**. The failure is silent and misleading:
- `<ItemContent>` is not recognised as a template parameter, so no context variable is generated → `CS0103: The name 'context'/'<your-context>' does not exist` on every reference inside the row.
- `Items="..."` does **not** error (it's just an HTML attribute string), which sends you hunting in the wrong place (Context name, generic inference, `TItem`, nested-type resolution — all red herrings).

**How to apply:** when a `<Virtualize>` (or any component) throws `CS0103` on its context variable while its other attributes compile fine, the component isn't resolving — check the page has the right `@using` before touching anything else. Confirmed 2026-06-12 building the Library group-list virtualization (PR2 of the Library nav rework); ~12 build cycles lost before spotting the missing line by diffing against `Authors/Index.razor`. Relates to [[mudblazor-menu-popover-gotchas]].

## feedback_single_select_explicit_add


When a lookup field lets the user "pick an existing one OR create a new one", make the create an **explicit gesture**, not an inferred one. The clean controls in this codebase (author/contributor chips) work because the user takes a deliberate action — Enter/comma adds a chip, and "create if it doesn't exist" is unambiguous *because the user committed*. The single-select series field was the last one still committing on **blur** (a guess at "is the user finished"), and it was the source of every series bug in the TD-15a follow-on: order carryover, a false-green "will attach" banner, and two regressions a clear-on-change patch introduced.

**The meta-lesson (the tell):** when you find yourself patching state-sync bugs on the *same control* more than once — invalidate this field on that change, clear that flag when this clears — stop patching the symptoms. The control's **commit gesture is wrong**. A field whose value mutates on every keystroke / blur, shared by two writers, will keep desyncing; an explicit single commit point removes the whole class.

**The fix / reusable pattern — `CreatableAutocomplete`** (`BookTracker.Web/Components/Shared/`): a single-select typeahead where
- `CoerceValue="false"` → free text never commits on its own (a pause to think / half-typed name is never a save point);
- `SearchFunc` returns existing matches and the component appends a synthetic **`Add "{query}"`** row when the typed query has no exact match;
- only an explicit selection (click / Enter on a row) fires `ValueChanged` with the committed name (existing pick, the typed new name, or null on clear);
- the parent owns id-resolution (cache-hit → pin; the "Add" row → eager-create + cache).

Used by the series field (`OnSeriesChosenAsync`) and both publisher fields (`OnPublisherChosenAsync`). This made the carryover/false-green class **structurally impossible** instead of patched — there is no free-edit of a committed name, and "chosen vs not" is just the value being non-blank (no separate accepted-flag to drift). It's the single-select sibling of the chip gesture.

**Razor gotcha that bit this:** passing the value as `Value="VM.Prop"` to a *string-typed* component parameter binds the **literal string** "VM.Prop"; it needs `Value="@VM.Prop"`. The old field hid this with `CoerceText="false"`; the new component coerces text so it rendered the literal. Always `@` the value expression.

Related: [[feedback_mudblazor_valuechanged_not_commit]] (ValueChanged fires per keystroke — the original "no commit signal" lesson), [[feedback_add_with_optional_existing]] (single entry point with free-text fall-through), [[feedback_mudautocomplete_capture_phase]] (Enter-handling escalation if a near-match steals the typed name), [[retro_td15a_eager_create_arc]].
