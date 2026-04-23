---
name: Duplicates — Author merge
description: PR 2 of the dedup series. Transactional merge with alias-compatibility refusal; two InMemory-provider footguns surfaced during tests.
type: project
---

## Shipped

PR #86 — 11 files. `/duplicates/merge/author/{a}/{b}` side-by-side review page with winner-radio, impact preview, transactional merge, redirect back to `/duplicates` with a banner describing what happened. Alias-incompatible pairs (different canonicals) are refused up-front with actionable guidance pointing at `/authors`.

## Surprise

- **InMemory doesn't support transactions, and the warning is elevated to an error by default.** First test run: six failures at `BeginTransactionAsync`. Fix: suppress `InMemoryEventId.TransactionIgnoredWarning` in `TestDbContextFactory`. One-line config change. Transactions still run on production SQL Server; InMemory silently no-ops them. Worth remembering: when adding any service that wraps work in a transaction, the test factory is the place to handle it, not the service.
- **InMemory doesn't expose pending in-memory changes to subsequent queries in the same context.** Test failure: `MergeAsync_promotes_winner_when_winner_was_alias_of_loser` — winner's `CanonicalAuthorId` ended up pointing at itself instead of null. Traced to: the "reassign aliases of loser" query ran *after* we'd nulled `winner.CanonicalAuthorId` in memory, but the query still saw the pre-change value and returned the winner as one of loser's aliases, then the reassignment loop pointed it at itself. Fix: explicitly exclude the winner ID in the aliases query. Semantically correct regardless of provider — winner's canonical status is handled separately above.
- **Two kinds of incompatibility I conflated on the first pass.** Initially I thought "same `CanonicalAuthorId` on both (including both null)" covered all the safe cases. But "loser is an alias of winner" and "winner is an alias of loser" are *also* safe merges (and common — "I marked one as alias by mistake, now I want to just collapse them"). Widened the compatibility matrix to four cases. The second case (winner-is-alias-of-loser) is what required the in-memory exclusion fix above — the two surprises are linked.

## Lesson

- **Think about in-provider query semantics when mutating + re-querying in the same context.** Rule of thumb: if you've changed a field in memory but haven't `SaveChanges`-d, subsequent LINQ queries against that field should be treated as *might* see the pre-change value. Either `SaveChanges` between the mutation and the query, or filter explicitly in the query to exclude the already-handled entity. The latter is faster and makes the intent explicit in the code.
- **Compatibility matrices deserve named cases, not just a single ternary.** The "both same canonical OR one alias of the other" rule looks simple but has four distinct shapes (both canonical, both alias of same, winner alias of loser, loser alias of winner). Each has subtly different cleanup needs — especially "winner alias of loser" which requires the winner promotion step. Writing the rule as case-numbered comments in the service kept the logic honest.
- **Query-param banners on redirect beat cross-page state.** Blazor's VM transience plus route navigation means "message on the target page" is tricky. A few `[SupplyParameterFromQuery]` properties on the target page is the cheapest way to carry "what just happened" across a nav. Worth reusing for the remaining merge PRs.
- **Test factory as a global knob.** `TestDbContextFactory` was already the single point where InMemory semantics get configured. When I hit the transaction warning, fixing it there (one line) rather than in the service (branches, `Database.IsInMemory()` checks, conditional paths) kept the service clean and matched the existing pattern. If future merge PRs add similar quirks, this is the place.

## Quotable

"Failing test: winner's CanonicalAuthorId = 2." Winner IS 2. The winner had been instructed to be an alias of itself. InMemory's query visibility semantics and a merge step that looked innocent in isolation conspired to produce the most literal self-reference bug I've seen in a while. The fix was one `&& a.Id != winner.Id` clause. The lesson — don't query mutated fields before save — is one of those things you nod at in the docs and then forget until you hit it.
