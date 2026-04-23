---
name: Retro — MVVM refactor + first tests
description: PR #24 — extracted ViewModels from Razor @code blocks; introduced BookTracker.Tests project with 24 tests
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit message of PR #24 (2026-04-15).

**Shipped** — moved all business logic, state, and data access out of Razor `@code` blocks into standalone C# ViewModel classes under `BookTracker.Web/ViewModels/`. Components became thin binding layers. Eight VMs ported (Home / BookForm / CopyForm / GenrePicker / BookList / BookAdd / BookEdit / BulkAdd). All registered as transient. New `BookTracker.Tests` project with 24 tests (xUnit + NSubstitute + EF InMemory) covering HomeViewModel, BulkAddViewModel, GenrePickerViewModel. CI pipeline gained a `dotnet test` step.

**Surprise (inferred)** — this is where testability got introduced. The pre-MVVM Razor components couldn't be cleanly tested because state + DB access + UI binding all sat in `@code`. Splitting VMs out wasn't just an architectural preference — it was the move that made the rest of the project's tests possible. Every later test file in `BookTracker.Tests/ViewModels/` rests on this PR.

**Lesson** — the right time to refactor for testability is *as soon as you want a single test*. Doing the split mid-project (PR #24, after 23 prior PRs of working code) was probably the right call — premature MVVM on day 1 would have been overkill, but waiting until PR #50 would have meant rewriting every existing component to test new ones. Trigger: "I want to write a test for something" → "OK, but the code shape doesn't allow it" → refactor first.

**Quotable** — adding `dotnet test` to CI in the same PR meant tests were never optional; they ran on every PR from then on.
