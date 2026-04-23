---
name: Trove (NLA) as third-line ISBN lookup
description: Added Trove as a coverage-of-last-resort provider after OL + GB; silent-skip-when-key-missing kept the keyless dev loop intact.
type: project
---

## Shipped

PR #84 (single PR, ~14 files). `BookLookupService` now chains Open Library → Google Books → Trove. Trove is skipped silently when no API key is configured. Full infra plumbing mirrors the Anthropic-key pattern end-to-end (`deploy.ps1 -TroveApiKey` → KV secret → KV-ref app setting).

## Surprise

- The reported "ISBN doesn't scan" bug wasn't a scanner or validation issue at all — the scanner accepts any EAN_13, and `BookLookupService` only length-checks (10/13). The real miss was metadata-catalog coverage: `978-0-6458xxx` is a self-published Australian Thorpe-Bowker range that neither OL nor GB indexes well. Diagnosis took longer than the fix.
- Original instinct was WorldCat as the fallback. Turned out WorldCat Search API v2 requires OAuth2 — ~4–5 files of plumbing before touching the UI. Swapping to Trove (simple API key, targets exactly the Australian long tail) was objectively better for the actual case *and* cheaper to integrate.
- Trove v3 returns multi-valued fields as arrays *or* single strings depending on cardinality — a known long-standing API wart. `JsonElement` + a `TroveStringValues` helper handles both shapes; tests pin the behaviour with both array and string fixtures.

## Lesson

- **Diagnose the miss before choosing the provider.** The user said "printed outside USA/Europe" but the real attribute was "self-published / long tail." Picking Trove (which targets that specifically) instead of the generic WorldCat was a direct consequence of framing the miss correctly.
- **"Silent skip when key missing" is load-bearing design, not a convenience.** It meant the branch was safe to merge before the NLA key arrived (registrations take days). Post-merge validation is a `TODO.md` entry, not a merge blocker. Same pattern as AI providers auto-detecting from config.
- **Mirror an existing provider when adding a new one.** The Anthropic-key plumbing across `deploy.ps1` / `main.bicep` / `resources.bicep` / `keyvault.bicep` / `app-config.bicep` gave a five-file template to copy. No surprises; each file took a minute.
- **Defer "graceful miss UX" even when it's tempting.** The original plan had a second strand (auto-focus title, reword message when all providers miss). Cutting it meant one focused PR instead of a bundled change. The miss UX can stand alone and ship later if the coverage gap doesn't close it on its own.

## Quotable

The user's framing of the bug — "having an issue scanning some isbn numbers, possibly if printed outside the usa/Europe" — was 80% right about the symptom and 100% wrong about the mechanism. Scanner, EAN_13, validation: all fine. The actual failure was two catalogs deep in a chain the user couldn't see. A useful reminder that bug reports describe experience, not root cause, and "printed outside Europe" translated to "self-published long tail" once the ISBN was decomposed.
