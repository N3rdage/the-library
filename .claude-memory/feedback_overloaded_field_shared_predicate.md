---
name: feedback_overloaded_field_shared_predicate
description: "Overloading an existing column/field to carry a second meaning silently conscripts every existing reader; centralise \"what does this value mean\" in one shared predicate (or model the new concept explicitly)."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 90df9d68-a582-4a86-895a-e1efab27cd96
---

When you repurpose an existing field to also carry a NEW concept, every existing consumer of that field keeps reading it with the OLD assumption — and the type system can't see the difference, so nothing flags the mismatch.

**The case that taught it (2026-06 series-order arc, [[retro_series_order_arc]]):** `Work.SeriesOrder` meant "this is numbered volume N." To make interquels (Edgedancer "4.5") sort beside their neighbours, the parser floored "4.5" → `SeriesOrder` 4 and kept the label in a new `SeriesOrderDisplay`. That floored int silently made the interquel *claim numbered slot #4* in gap detection across three independent consumers (web AI-profile gap text, wishlist gap card, mobile `GetSeriesGaps`), masking a genuinely-missing real #4. The review caught it as a regression in the same PR that added the feature.

**Why:** the new meaning ("this int is a floored sort key, not a real volume") is invisible at every call site that reads the old field. Hand-copied guards drift — the mobile guard ended up missing the range clamp the two web guards had.

**How to apply:**
- When a fix overloads a field, list every existing reader of that field and ask "does my new meaning break this reader's assumption?" before shipping.
- Prefer modelling the new concept explicitly (a separate column) when the field's type can't express it.
- When that's overkill, centralise the interpretation in ONE shared predicate the readers all route through (e.g. `SeriesSlots.OccupiesNumberedSlot(order, display)` in `BookTracker.Shared`, callable from web + the mobile cache which can't reference Web) — so "what does this value mean?" has a single answer the type system points everyone at, not N hand-copied conditions.

Pairs with [[feedback_common_path_over_visible_edge]] (audit the silent majority a change touches).
