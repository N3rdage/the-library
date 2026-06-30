---
name: retro_td15a_eager_create_arc
description: TD-15a eager-create-at-picker arc retro — 4 PRs making lookup creation eager at the commit gesture; durable lessons on MudBlazor commit signals, eager-create-only-new, Option B, and the missing-picker gap.
metadata:
  type: reference
  node_type: retro
---

# TD-15a eager-create arc retro

Closed the last strand of TD-15: move lookup creation **eager to the picker** so the aggregate save stops owning the insert and the (single-user, near-impossible) check-then-insert race dissolves on the interactive paths. 4 PRs, 2026-06-28 → 06-30. PRs: #392 author/contributor, #395 publisher, #396 series + manual-series-on-Add, + close-out. State: [[project_td15a_eager_create_arc]]; resolution recorded in `docs/TECH-DEBT.md` TD-15 → Resolved.

**Design = Option B (Drew's call).** Pickers eager-create on commit; the save **keeps `*Resolver.ResolveAsync` as a net** (covers ISBN-prefill names that bypass the picker). No names→ids contract change → far less churn than a load-only save, and the PRs stay independent (saves untouched, no convergence on `BookAddViewModel.SaveAsync`).

## Durable lessons

1. **MudBlazor `ValueChanged` is not a commit signal.** Under `CoerceValue="true"` it fires on **every keystroke**, so hanging eager-create on it created a publisher row per partial string ("spits in t", "spits in th", …). There is no "user is finished" point inside `ValueChanged`. Bind the value with plain `@bind-Value` and trigger the eager action from an **explicit gesture** — `OnBlur` for free-text typeaheads, Enter/comma for chips, a button for the suggestion-accept. This is the headline lesson: [[feedback_mudblazor_valuechanged_not_commit]]. It shipped to dogfood before Drew caught it — free-text autocomplete side-effects are subtle enough to warrant a manual try before declaring done.

2. **Eager-create only NEW, not existing picks.** The first publisher cut dispatched `CreatePublisher` on *every* commit — a DB round-trip even when picking an existing one (Drew: "every UI click is an upsert/wire call"). Fix per control: a **cached-membership check** (publisher + series typeaheads cache the small lookup list and skip the call for a known name) or a **pick-vs-type signal** (the author dropdown pick is always existing → skip; only typed Enter/comma can be new → create). Caching the list also killed the per-keystroke DB search the dialog had.

3. **The commit gesture differs per control — match the control's natural "done".** Chip Enter/comma (author free-text), dropdown-pick (author existing, no create), `OnBlur` (publisher + series free-text typeahead), Accept button (series suggestion banner). There is no single pattern; the contributor picker has only a single "Add" button (no pick-vs-type signal), so it keeps an idempotent dispatch — an accepted asymmetry, not a miss.

4. **"Make creation eager at the picker" presupposes a picker exists.** The biggest value in the arc wasn't the eager-create plumbing — it was discovering (via Drew's dogfood) that **single Add Book had no series control at all**: series only arrived through the ISBN-lookup suggestion banner, so a book whose lookup named no series couldn't be put in one until after saving. The fix (a manual series typeahead bound to the same `AcceptedSeries*` state the banner writes — one source of truth) closed a real gap. Lesson: when a tech-debt item says "do X at the picker", verify the picker is actually there for every entry path before scoping.

5. **An idempotent find-or-create command is distinct from a strict create.** Series already had `CreateSeries` (full fields + duplicate rejection, for the /series form). Accepting a suggestion / committing a typeahead must be **idempotent**, so it needed a separate `EnsureSeries` (find-or-create → id via `SeriesResolver`). Don't force a strict-create command to carry an idempotent path — name and separate them (`CreateAuthor`/`CreatePublisher`/`EnsureSeries` are all find-or-create→id; `CreateSeries` stays strict).

6. **Best-effort eager-create + save-net is the safety belt.** Every eager dispatch is wrapped try/catch-log; the save's resolver still guarantees the row. This made the whole arc low-risk: a transient fault, the accepted race, or a missed blur all fall through to the same net that existed before the arc.

7. **Repeated state-sync patching on one control = the commit gesture is wrong (the close-out's big lesson).** The arc-end review caught a real carryover defect in the series field (#396); the first patch (clear order/flag on name change) introduced two regressions of its own. The tell: I was patching symptoms on the *same control* over and over. The root cause was that series was the last single-select free-text field still committing on **blur** — a guess. Replacing it with an **explicit gesture** (`CreatableAutocomplete`: pick existing, or select a synthetic `Add "…"` row; `CoerceValue="false"` so typing never commits) made the whole carryover/false-green class *structurally impossible* rather than patched, and unified series + publisher with the chip controls' "the user takes the action" philosophy. Full lesson: [[feedback_single_select_explicit_add]]. Shipped as #399 (series) + the publisher follow-on.

## Process notes

- **Per-PR reviews were Drew-triggered, not the arc-end default.** Drew asked for reviews on PR1/PR2 explicitly; PR3 + manual-series + close-out were not separately reviewed. The arc-end high-effort review (the [[feedback_review_at_arc_end]] default) **ran at close and earned its keep** — it caught the series carryover defect in merged #396 that dogfood missed (the value was plausible and on-screen; only fired on change-from-A-to-B). That triggered the gesture redesign (lesson 7). Reinforces running the arc-end review even when each PR felt clean.
- **`.claude-memory`/profile/branch-sync friction.** Committing memory mid-arc and then switching branches reverted the auto-loaded profile copy (the branch's older `.claude-memory` synced back over it), and an empty file got created by a failed `git show >` redirect. Lesson: keep memory updates on **one** chore branch (or the arc-close PR per the exception), branch from fresh `main`, and don't leave `.claude-memory` changes uncommitted across branch switches. See [[feedback_memory_commits]].
- **Three iterations on one field (publisher):** dispatch-on-commit → only-new → on-blur. Free-text autocomplete eager actions earned every iteration; budget for a dogfood pass on them.
