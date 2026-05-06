namespace BookTracker.Tests;

/// <summary>
/// Test category constants for <see cref="Xunit.TraitAttribute"/> usage.
/// CI filters tests by category; the convention is whitelist (you opt
/// IN to the categories a job runs) rather than blacklist (forgetting
/// to exclude a new category type is the failure mode).
///
/// See <c>BookTracker.Tests/README.md</c> for what each category means
/// and how to run subsets locally.
/// </summary>
public static class TestCategories
{
    /// <summary>Pure C# logic. No DB, no Razor render, no browser. Fast (&lt;10ms each).</summary>
    public const string Unit = "Unit";

    /// <summary>bUnit Razor render with MudBlazor wired. Mid-tier; fast but heavier than Unit.</summary>
    public const string Component = "Component";

    /// <summary>Real EF Core against Testcontainers SQL Server + Respawn. ~24s suite startup, fast per-test thereafter.</summary>
    public const string Integration = "Integration";

    /// <summary>Playwright browser tests. Slowest tier — full request/response including JS interop.</summary>
    public const string E2E = "E2E";

    // Reserved for future use (perf / load-shape testing). No tests use it yet.
    public const string Load = "Load";
}
