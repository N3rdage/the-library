using Xunit;

// Tests share the SqlServerContainer's database (one schema, wiped between
// TestDbContextFactory instantiations). Parallel execution would have tests
// trampling each other's data, so we run serially. Wipe-per-test takes
// ~50-150ms on Testcontainers; serial run for ~322 tests ≈ 30-60s total
// — acceptable for PR-time, well within budget.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
