---
name: feedback_single_select_explicit_add
description: A single-select free-text lookup that commits on blur is structurally fragile — make creation an EXPLICIT gesture (a synthetic "Add …" row) instead. Repeated state-sync patching on one control is the tell that its commit gesture is wrong.
metadata:
  type: feedback
  node_type: memory
---

When a lookup field lets the user "pick an existing one OR create a new one", make the create an **explicit gesture**, not an inferred one. The clean controls in this codebase (author/contributor chips) work because the user takes a deliberate action — Enter/comma adds a chip, and "create if it doesn't exist" is unambiguous *because the user committed*. The single-select series field was the last one still committing on **blur** (a guess at "is the user finished"), and it was the source of every series bug in the TD-15a follow-on: order carryover, a false-green "will attach" banner, and two regressions a clear-on-change patch introduced.

**The meta-lesson (the tell):** when you find yourself patching state-sync bugs on the *same control* more than once — invalidate this field on that change, clear that flag when this clears — stop patching the symptoms. The control's **commit gesture is wrong**. A field whose value mutates on every keystroke / blur, shared by two writers, will keep desyncing; an explicit single commit point removes the whole class.

**The fix / reusable pattern — `CreatableAutocomplete`** (`BookTracker.Web/Components/Shared/`): a single-select typeahead where
- `CoerceValue="false"` → free text never commits on its own (a pause to think / half-typed name is never a save point);
- `SearchFunc` returns existing matches and the component appends a synthetic **`Add "{query}"`** row when the typed query has no exact match;
- only an explicit selection (click / Enter on a row) fires `ValueChanged` with the committed name (existing pick, the typed new name, or null on clear);
- the parent owns id-resolution (cache-hit → pin; the "Add" row → eager-create + cache).

Used by the series field (`OnSeriesChosenAsync`) and both publisher fields (`OnPublisherChosenAsync`). This made the carryover/false-green class **structurally impossible** instead of patched — there is no free-edit of a committed name, and "chosen vs not" is just the value being non-blank (no separate accepted-flag to drift). It's the single-select sibling of the chip gesture.

**Razor gotcha that bit this:** passing the value as `Value="VM.Prop"` to a *string-typed* component parameter binds the **literal string** "VM.Prop"; it needs `Value="@VM.Prop"`. The old field hid this with `CoerceText="false"`; the new component coerces text so it rendered the literal. Always `@` the value expression.

Related: [[feedback_mudblazor_valuechanged_not_commit]] (ValueChanged fires per keystroke — the original "no commit signal" lesson), [[feedback_add_with_optional_existing]] (single entry point with free-text fall-through), [[feedback_mudautocomplete_capture_phase]] (Enter-handling escalation if a near-match steals the typed name), [[project_td15a_eager_create_arc]].
