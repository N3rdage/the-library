---
name: feedback_mudblazor_valuechanged_not_commit
description: MudAutocomplete ValueChanged fires per keystroke (CoerceValue) — never treat it as a commit; eager actions need an explicit gesture (OnBlur/Enter).
metadata:
  type: feedback
---

A MudAutocomplete's `ValueChanged` is **not** a "user is finished" signal. With `CoerceValue="true"` it fires on **every keystroke** (the typed text is coerced into the value continuously). Wiring an eager side effect (a DB write, a dispatch) to `ValueChanged` therefore runs it per character — e.g. eager-creating a publisher on commit produced rows `"spits in t"`, `"spits in th"`, `"spits in the"`, … as Drew typed "spits in the eye" (TD-15a PR2, 2026-06-29).

**Why:** there is no detectable "done" point inside `ValueChanged`. Pausing to think or fixing a typo is indistinguishable from finishing. Free-text autocompletes (no chips) don't emit a discrete commit the way the chip picker does (Enter/comma via the JS capture layer — [[feedback_mudautocomplete_capture_phase]]).

**How to apply:** for any eager/persisting action behind a free-text autocomplete, bind the value with plain `@bind-Value` (typing just updates the string, no side effect) and trigger the action from an **explicit gesture** — `OnBlur` (field lost focus) is the robust one; Enter via a keydown handler if needed. On blur, read the now-settled bound value, then act. Keep it best-effort (the save-time find-or-create net still guarantees the row if blur is missed). General rule: **stop trying to be clever with MudBlazor change events — only act on a gesture that genuinely means "the user is finished"** (Drew, 2026-06-29). Same family as [[feedback_mudblazor_menu_popover]] and [[feedback_dialog_vm_lifetime]] — MudBlazor's event surface rarely matches the intuitive mental model.

Context: TD-15a eager-create arc ([[project_td15a_eager_create_arc]]).
