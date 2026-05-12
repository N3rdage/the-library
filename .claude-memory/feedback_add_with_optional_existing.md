---
name: "Add, but allow me to select existing" UX pattern
description: Drew's preferred shape for any "add X" flow where an X might already exist — one entry point with type-to-discover-existing, free-text falls through to capture new. Don't put two buttons.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When designing an "add X" surface where X might already exist in the library (existing Work, existing Author, existing Series, …), do NOT split into two buttons ("Add new" / "Pick existing") or two stages ("first search, then if no match show the new form"). Use one entry point: a typeahead-y title input that surfaces matching existing rows as the user types, with free-text fall-through to capture-as-new.

**Why:** Drew's testing-feedback during the Add/View/Edit polish arc, 2026-05-13. Two buttons "faces the same problem as for Add — far better to be able to do a 'Add, but allow me to select an existing if one exists' pattern." The cost of always-search is zero keystrokes (debounced, server-side); the cost of guessing wrong on the toggle is a broken flow. Drew steered me to this twice in the same arc (Add Book collection rows in PR 5, View-page Add Work in PR 6) before I memorised the pattern. Shipped as `MudAutocomplete<WorkSearchResult>` + capture-phase JS suppression of auto-pick (see `feedback_mudautocomplete_capture_phase.md`).

**How to apply:** when planning a new "add an X to Y" surface, default to:
- Single input field (autocomplete shape).
- Suggestions appear from existing rows as the user types (≥2 chars typical, 250ms debounce).
- Down + Enter on a highlighted suggestion → attach existing (skips the create form).
- Free-text Enter / Tab away from suggestions → falls through to the create path (inline form fields, or "Save" button reveal).
- A confirm step only if the attach-vs-create distinction has destructive consequences (e.g. fat-finger flick on a toggle that discards in-flight attachments — see `Add.razor`'s collection toggle confirm).

If a stakeholder asks for "two buttons" or "a mode toggle," push back with this shape unless there's a concrete reason the discovery cost is real (e.g. the existing list is so dense that typeahead is noisy — rare).
