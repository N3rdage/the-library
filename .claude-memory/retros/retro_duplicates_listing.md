---
name: Duplicates — detection + listing page
description: PR 1 of the dedup series. Conservative exact-match detection, /duplicates page with tabs + dismiss/unignore. Merge actions deferred to PRs 2–5.
type: project
---

## Shipped

PR #85 — 20 files, additive migration, 150→154 tests. `/duplicates` route with tabs for Authors / Works / Books / Editions, each listing candidate pairs with a Dismiss button and a collapsible "Dismissed (N)" section offering Un-ignore. Detection is recompute-on-load (no caching) and runs in a single O(n) group-by pass per entity type. `IgnoredDuplicate` table is polymorphic across all four types (no FK); orphans swept lazily during each detect call.

## Surprise

- **The Razor `@<text>` literal inside a lambda body is a compile-or-semantic trap.** I first wrote `private RenderFragment RenderAuthorTab() => __b => { <text>...</text> }` — the compiler accepts it, but it doesn't actually work the way you'd expect because `@<text>` literals need to be the direct body of the fragment expression, not wrapped in lambda braces. Discovered it looking at the generated code. Refactored to small sub-components (`PairCard.razor`, `AuthorSide.razor`, …) which is more idiomatic Blazor anyway — tighter per-file logic and each subcomponent gets a real `[Parameter]` contract.
- **EF InMemory chokes on `Nullable<DateOnly>.Value.Year` even behind a null guard.** Tests failed with `"Nullable object must have a value."` at the LINQ translation layer, not in my code. InMemory's expression visitor tries to access `.Value` regardless of the ternary guard. SQL Server would handle it fine, but we need the test suite to pass. Fix: project the raw `DateOnly?` and compute `.Year` client-side after `ToListAsync()`. Worth remembering: InMemory's semantic-fidelity gap shows up in subtle ways; when a pattern that "should work" fails only under InMemory, try splitting the projection into server-side + client-side halves.
- **"Deliberately conservative" lasted one user session.** I planned exact-after-normalisation only, got Drew's sign-off explicitly ("happy to even edit names to force matches"), shipped it, and the second message back was "too conservative — Doug Preston, Douglas Preston, D Preston should match". Loosened on the same branch with a follow-up commit introducing a surname+first-initial strategy alongside exact. Both strategies run; tighter one wins the `MatchReason` when both apply. Not a planning failure — the conservative default was the right shipping call, and loosening after first contact is the point of shipping small.

## Lesson

- **Conservative defaults then iterate beats aggressive defaults then untangle.** Shipping exact-match only, getting it in Drew's hands, and tuning after he saw the signal/noise ratio was measurably faster than designing the "right" fuzzy threshold upfront. The two-commit shape of this branch (detect-exact → loosen-for-Preston-triplet) is the template for future matcher work.
- **Polymorphic dismissal table without FK was the right call.** Four entity types sharing one `IgnoredDuplicate` table, discriminated by `EntityType` enum, with lazy orphan sweep instead of cascade deletes. Saved 3 tables + 12 FKs for a feature where the cleanup cost is trivial. The "correct" normalised design would have been more code, more migrations, and more test surface area for zero user-visible benefit.
- **Don't inline RenderFragments; split to components.** Razor's syntax for RenderFragment-as-local-variable or RenderFragment-returning-method is awkward enough that small subcomponents (`PairCard.razor`, the four `XxxSide.razor` files) are both cheaper and more readable. One subcomponent per role, `[Parameter, EditorRequired]` to pin the contract. Six files in a `Duplicates/` folder is fine.
- **Plan-first gave a good estimate.** Predicted ~14 files for PR 1; actual was 20 (6 subcomponents I didn't plan for). The shape was right, the count was light. Next time when I plan subcomponents, assume the Razor page count doubles.

## Quotable

One planning round: decide "exact-only, deliberately conservative, user can edit names to force matches." Get explicit user sign-off. Ship. Use it for ten minutes. First comment: "too conservative — Doug Preston and Douglas Preston should match." Reality survives contact with users about as long as a plan survives contact with the enemy.
