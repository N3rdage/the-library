---
name: feedback_mudblazor_menu_popover
description: MudMenu inline activator + popover-vs-stopPropagation gotchas (MudBlazor v8+) — what makes a row-action menu actually open.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 6ab5419b-17ad-4eee-aec9-0d6d84d6855c
---

Inline row-action menus (e.g. the Library status quick-set) in MudBlazor v8/v9 have three traps that each silently produce a button that "registers the click but no menu appears". All three cost real iterations on the `/books` status menu (2026-06, MudBlazor 9.4.0):

1. **`MudButton` inside `<ActivatorContent>` stops click propagation by default**, so the click never reaches `MudMenu`'s toggle and the menu never opens. Fix: use MudMenu's **own** activator — `<MudMenu Label="..." Variant="..." Color="..." EndIcon="...">` with the items as direct child content. MudMenu then owns the button + its click. Do NOT reach for `ActivatorContent` + a custom button for a menu.
2. **A click-less `MudChip` as the activator swallows the click** too (no `OnClick` → it consumes rather than forwards). Same symptom.
3. **Wrapping the menu in `<div @onclick:stopPropagation="true">` breaks the popover** — MudMenu/MudSelect popovers need the click to reach the document to stay open; stopPropagation opens-then-instantly-dismisses. `MudRating` survives the same wrapper because its stars are the directly-clicked element (no popover) — so "rating works but menu doesn't" is the tell.

**Why:** the misleading precedent is `EditionCoverUploadDialog`, where a `MudButton` in `MudFileUpload`'s `ActivatorContent` works fine — but `MudFileUpload` wires its activator via a label/hidden-input, not click-bubbling, so it doesn't generalise to `MudMenu`.

**How to apply:** for a row-action menu use the `Label`/`Icon` activator form. To keep inline-editable controls from triggering a clickable row's navigation, **don't** use a stopPropagation wrapper — decouple instead: put navigation on explicit child elements (cover image only — keeps the title selectable for copy/paste) rather than a whole-row `OnRowClick`. Related: [[feedback_mudautocomplete_capture_phase]], [[feedback_dialog_vm_lifetime]].
