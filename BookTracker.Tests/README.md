# BookTracker.Tests

Test project for BookTracker. xUnit + Testcontainers SQL Server + bUnit + (planned) Playwright.

## Test category contract

Every test class must carry a `[Trait("Category", TestCategories.X)]` attribute. CI filters by category — the convention is **whitelist** (you opt IN to which categories a job runs) rather than blacklist (forgetting to exclude a new category type is the failure mode).

The constants are defined in [`TestCategories.cs`](TestCategories.cs):

| Constant | What | Speed | Examples |
|---|---|---|---|
| `Unit` | Pure C# logic. No DB, no Razor render, no browser. NSubstitute mocks for collaborators are fine. | <10ms each | `ErrorMessageMapperTests`, `UserTelemetryInitializerTests`, the merge-VM tests using mocked services |
| `Component` | bUnit Razor render with MudBlazor wired. Mid-tier — fast but heavier than `Unit` because of component-rendering setup. | mid-tier | `MudAuthorPickerTests` |
| `Integration` | Real EF Core against `Testcontainers.MsSql` + Respawn. Slower per test once the container is up. | ~24s suite startup; fast per-test thereafter | most `ViewModels/*Tests`, most `Services/*Tests` |
| `E2E` | Playwright browser tests. Slowest tier — full request/response including JS interop. | minutes | (none yet — the Playwright POC adds the first) |
| `Load` | _Reserved_ for future perf / load-shape work. No tests use it yet. | n/a | n/a |

Trait at the **class level**, not the method level. A few classes have mixed-need methods (e.g., `GenrePickerViewModelTests` has both pure-logic and DB-using methods); class-level tagging picks the slowest of the methods, which is the safe side to err on.

## Running subsets

Run all tests in a category:
```powershell
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=E2E"
```

Combine multiple categories (xUnit's filter syntax uses `|` for OR within a key):
```powershell
dotnet test --filter "Category=Unit|Category=Component"
dotnet test --filter "Category=Unit|Category=Component|Category=Integration"
```

Fast-feedback loop while iterating on a single test class:
```powershell
dotnet test --filter "FullyQualifiedName~MudAuthorPickerTests"
```

## CI behaviour

The PR job in `.github/workflows/ci.yml` runs `Category=Unit|Category=Component|Category=Integration` — i.e. everything except `E2E` and `Load`. When the Playwright POC ships, `E2E` will get its own job (likely scheduled / on-demand rather than per-PR, given the runtime cost).

## Testcontainers prerequisite

Integration tests use a single process-scoped SQL Server container (`SqlServerContainer.cs`) reused across the test run, with Respawn wiping data between tests. Docker Desktop must be running locally for Integration tests to start.

## Adding a new test class

1. Pick the category that matches the slowest thing the class does (use TestDbContextFactory? → `Integration`. bUnit `RenderComponent`? → `Component`. Pure logic? → `Unit`.)
2. Add `[Trait("Category", TestCategories.X)]` immediately above the `public class` declaration.
3. Run `dotnet test --filter "Category=X"` to confirm it picks up the new test.

If you forget the trait, the test silently doesn't run in CI — that's the failure mode the whitelist convention chooses over the alternative (forgetting to exclude in a blacklist, which would slow CI but still catch bugs). Worth a glance at the test count after merge.
