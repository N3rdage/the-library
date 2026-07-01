---
name: feedback_grep_list_is_a_checklist
description: After a grep/search returns a list of files for a mechanical sweep, treat every entry as a checklist item — fix it or explicitly classify it — don't act on the first hit and move on.
metadata:
  type: feedback
---
After a grep / codebase-search returns a list of hits, that list is a **checklist**, not "found the main one." Act on every entry — change it, or explicitly classify it (skip-because-X / false-positive / not-applicable) — before moving on. A file that appears in the result but not in your diff is a smell to justify, not ignore.

**Why:** In the series-on-Book cutover (TODO #56, PR2) my own grep for `SeriesId` surfaced `AIAssistantViewModel`, `AIAssistantService` (×3 providers), and `SeriesMatchService` — and I acted on `SharedParsers` only. The round-1 code review then found **3 confirmed silent-failure bugs** in exactly those listed-but-untouched files (AI "create collection" wrote the soon-dead Work column → silent no-op; AI "suggest collections" + series-match Strategy 2 read stale Work-level series). The grep had already pointed straight at them; the failure was treating its output as a *discovery* rather than a *worklist*. This is especially dangerous in an atomic cutover, where a missed reader fails **silently** (serves stale data) instead of loudly.

**How to apply:** When a search drives a mechanical sweep (rename, field/ownership move, API change), enumerate each hit and tick it off — changed / skipped-with-reason / not-applicable — rather than scanning for "the obvious one." When the sweep removes a member, lean on the compiler: dropping the column/method turns the same list into hard errors you can't skip (the contract-migration trick from the same arc). Related: [[feedback_overloaded_field_shared_predicate]], [[retro_series_on_book_arc]].
