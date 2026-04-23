---
name: Retro — AI integration arc (Anthropic-only → multi-provider with toggle)
description: PRs #45-#61 introduced the AI assistant, then refactored to multi-provider (Anthropic / Foundry / Azure OpenAI)
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
> **Reconstructed from git history.** Drawn from commit messages of PRs #45 through #61 (2026-04-17 to 2026-04-18).

**Shipped** — eight feature PRs plus a multi-provider refactor:
- PR #45 — first AI integration (Anthropic SDK, prompt caching, genre suggestions constrained to the preset taxonomy)
- PR #46 — `/assistant` page with genre cleanup section
- PR #47 — collection cataloguing (AI suggests Series/Collection groupings)
- PR #48 — shopping suggestions based on library patterns
- PR #49 — book advisor with suitability assessment (Opus, deep mode)
- PR #52 — photo ISBN OCR via Sonnet vision (covered separately in `retro_bulk_capture.md`)
- PR #54 — multi-provider refactor: `AIProviderFactory`, `IAIAssistantService` interface, AI options as flat `AI:` config section
- PRs #55, #57 — Azure Foundry + Azure OpenAI provider implementations
- PR #58 — runtime AI provider toggle on Assistant and Bulk Add pages
- PR #61 — rename `AzureFoundry` → `MicrosoftFoundry` (Microsoft's own rebrand)

**Surprise (inferred)** — the project went from "AI feature: genre suggestions" to "swap providers at runtime via dropdown" in three days. The PR #54 refactor (single provider → factory pattern) was relatively cheap because the per-feature methods (`SuggestGenresAsync`, `AssessBookAsync`, etc.) were already a clean interface. The interface had been written for testability, and the multi-provider refactor got it for free.

**Lesson** — `IAIAssistantService` was created in PR #45 just for testability ("we want to mock this in tests"). PR #54 turned that interface into the abstraction for swappable providers. Designing for testing → accidentally designed for runtime polymorphism. Worth noticing: the same shape that enables `Substitute.For<IAIAssistantService>()` in NSubstitute is the shape that enables `AIProviderFactory.Get(provider)`.

**Quotable** — the AI providers were planned as separate features but ended up as one architectural arc. Foundry got dropped from prod in this session (Sponsored subscription) but the abstraction remained because it costs nothing to keep.
