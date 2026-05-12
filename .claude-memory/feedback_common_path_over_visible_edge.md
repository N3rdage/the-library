---
name: Optimise for the common path, not the visible edge
description: When fixing a reported edge-case bug, name what the fix changes for the silent common path before applying. Visible bugs get reported; the common path working fine is silent — the trade-off direction matters.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When a user reports an edge case failing, the reflex obvious fix often satisfies the visible bug at the cost of the silent common path. Before applying the obvious fix, name what the fix changes for the *unreported* majority case. If that path was working and the fix makes it worse, the fix might be net-negative — find an alternate path for the edge that doesn't disturb the common.

**Why:** 2026-05-12 — Drew's mobile testing surfaced "can't type X check digit in ISBN-10 manual entry." My proposed fix: change `Keyboard="Numeric"` → `Keyboard="Default"`. That satisfies the visible edge (X is typeable). It also costs the silent common path — typing 12-13 digits on the on-screen numeric keypad has big keys; the default keyboard cramps them to a small number row. Drew called this out before I shipped: *"80% of the time the numeric keyboard is incredibly more useful than the default... would be nice to have a way to use the numeric, with some alternate way of getting an X in there on the odd occasion."* Better fix: keep Numeric as default, add a small `+X` button for the edge. Same edge resolved; common path untouched.

**How to apply:**

- Before applying any obvious fix to a reported bug, name the silent path the fix changes. Compare its frequency to the reported edge.
- For UI: "what does the fix do to the keyboard / cursor / focus / scroll-position / default value for the unsurfaced majority?"
- For data: "what does the fix do to records that *aren't* the failing one?"
- Sister to the chip-picker / sledgehammer lessons but at an earlier layer — those are about *verification* ("am I right about the cause?"); this is about *what to optimise for* ("which trade-off direction am I picking?"). Both feed the same diagnostic discipline.
- Companion question: "What would the silent 90% want if we asked them?" The user reporting the bug is, by definition, in the surfaced minority for that flow.
- A clean alternate path (small button, toggle, long-press, secondary keyboard binding) for an edge case is almost always cheaper to live with than redesigning the common path around it.
