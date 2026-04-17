---
name: Testing conventions
description: Include minimal tests for new logic to prevent regression. Skip tests for pure markup/CSS.
type: feedback
originSessionId: f4ac39e5-d24f-4335-a75d-edce7263c131
---
Include a minimal set of tests to cover new ViewModel/business logic. The primary goal is regression prevention as complexity grows, not exhaustive coverage.

Pure markup/CSS changes don't need tests.

**Why:** The test suite is a safety net against regression, not a coverage target. Keep tests focused on logic that could break silently.

**How to apply:** When adding new VM methods or business logic, add tests that cover the happy path and key edge cases. Don't over-test trivial logic.
