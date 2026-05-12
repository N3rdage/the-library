---
name: MudAutocomplete capture-phase JS recipe (free-text Enter vs navigated pick)
description: When a MudAutocomplete needs "free-text Enter captures as new, Down+Enter on a suggestion attaches existing," MudBlazor 9.x auto-highlights the first match and commits it on Enter without navigation. Suppress via a capture-phase keydown listener that tracks user navigation per input.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
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
