---
name: Reusable instructions as a session deliverable
description: Two meta-level artefacts shipped as side products of bigger arcs — a git-history-rewrite prompt (extracted after the real rewrite on this repo) and a going-public prompt (extracted after the actual flip). Both distilled iterative "try and learn" work into step-by-step instructions another Claude Code session can execute in a different repo. Notable because they treat the prompt itself as a deliverable, not the code.
type: project
---

## Shipped

Two standalone instruction prompts, written as paste-into-another-session artefacts:

- **Git history rewrite** — full flow to remap author name + email + Co-authored-by trailers across history, using `git-filter-repo`. Covers installation, discovery, the rewrite itself, and every gotcha we hit on this repo (origin removal, stashed working tree, `--force-with-lease` not fitting after a rewrite, GitHub's squash-merge trailer pattern).
- **Going public** — flow from private repo to public, covering discovery, secret-scan with `gitleaks`, stranger's-eyes scrub (distinct from security scrub), planning doc with explicit options, pre-flip hygiene, GitHub Settings walkthrough (including the "features only appear after flipping" quirk), and post-flip housekeeping.

Both were extracted at Drew's request after the actual work on this repo finished. The real arcs took 5–10 iterations of try-hit-snag-fix-learn; the resulting prompts skip the learning phase and hand the next repo a pre-debugged answer.

## Surprise

- **The extraction step was nearly free, but only because the work had already happened iteratively.** Writing either prompt *before* doing the work would have been a lot harder — you'd be writing from documentation rather than from "here's what actually breaks when you try this." The iterative session on this repo caught the `co-authored-by` trailers that GitHub's squash-merge injects, the `--force-with-lease` refusing after a rewrite, the "Private vulnerability reporting" that only appears after the flip, the `.claude-memory/user_drew.md` being a surprising decision point. Not one of those is in the "obvious" set you'd include in a spec-first instruction. Doing it live and then extracting captures them for free.
- **Both prompts ended up with the same structural shape without intending to.** Neither was written with the other as a template, but when I look at the final instructions side-by-side: both start with "discovery" (read the repo state + ask the user a handful of questions), both have a tools-install section, both have explicit "stop and ask the user" checkpoints, both have a "what NOT to do" section, both end with a "durable lesson" paragraph. That's not coincidence — it's the shape of instructions-for-another-Claude that actually work. Might be worth codifying as a template for future reusable-prompt extractions.
- **The "decision points with explicit options" pattern showed up in both prompts.** Git rewrite has "name-only, email-only, or both?" / "keep history trailers or also rewrite them?" Going-public has "single repo vs split?" / "product-forward vs meta-forward README?" / file-by-file tracking decisions. Both prompts explicitly instruct the executing Claude to **enumerate options, ask the user, wait.** Not coincidentally, the going-public session we just finished produced the single biggest surprise when Drew picked Option A over my recommended B — which reframed the README. Building that structure into the instruction is how the next session also gets that option-extraction benefit rather than silently defaulting.
- **Drew's framing of the value is the clearest articulation of the workflow's upside.** In his own words: "a bit of a try and learn discussion in this repo" became "a simple step by step plan" for another repo. That's the loop: hard-won local knowledge gets packaged as portable prescription. The packaging isn't a side task; it's the deliverable. Most sessions produce code; these sessions produced Claude-readable instructions, and that's just as valid an output type.

## Lesson

- **Reusable instructions are a valid session deliverable alongside code. Bill them as such.** When a piece of work involves (a) a destructive or unfamiliar operation, (b) gotchas that aren't in the tool's documentation, (c) decisions that depend on user preference — finish by asking: "is this worth extracting as a prompt for another repo?" If yes, the extraction itself is a 15-30 minute task at the end of the session and the artefact pays forward indefinitely. The decision cost is tiny; the payoff is asymmetric.
- **Structural shape that works for reusable instructions:**
  1. **Boundary statement** — what the prompt is for + what it explicitly won't do (e.g. "you will not push; user handles all pushes").
  2. **Discovery + user questions** — read the repo, ask the user the handful of things that change the plan.
  3. **Tools install** — concrete commands for the platforms the user is likely on.
  4. **The work** — step-by-step, with the gotchas that the original iterative session hit.
  5. **Verify** — how the next Claude checks the result is what was intended.
  6. **Stop points / hand-off** — exactly where to stop and let the user act.
  7. **What NOT to do** — explicit guardrails against known failure modes.
  8. **Durable lesson** — the one-paragraph generalisation, so the prompt also teaches the principle not just the procedure.
  Both prompts converged on this; it's worth keeping as the shape for future extractions.
- **Not every session extracts.** Heavily project-specific work (e.g. "fix this NullReferenceException") does not generalise; forcing an extraction would produce a fragile prompt that doesn't survive contact with a different codebase. Test the extraction by asking "would this prompt work with the user's answers substituted, in a totally different stack?" If the answer is "yes, with maybe one parameter changed," extract. If "no, the whole middle is project-specific," don't — and that's fine, not every session's output needs to be a reusable tool.
- **Try-and-learn beats spec-first for destructive or unfamiliar operations.** The spec-first approach to git-history-rewrite would have pulled from `git-filter-repo`'s README, produced a plausible-looking instruction, and missed every gotcha we actually hit. Running it once in a real repo with a real clock-and-consequences took half the time, caught all the snags, and produced an instruction informed by what actually breaks. The "safe" approach (write a spec, don't touch anything until fully understood) is often the slower one when the spec leaves out the 10 things that only surface on contact. Apply this more generally: when the user wants to learn how to do a risky operation, doing it together in a real repo + extracting the prompt afterward is a better use of the session than writing the instruction speculatively up front.

## Quotable

The first time you do a destructive git operation, you discover things that aren't in the tool's README: filter-repo removes your origin remote, GitHub's squash-merge injects co-authored-by trailers you need to rewrite separately, `--force-with-lease` stops working after you rewrite a ref. You can't find these in the docs because they're gotchas, not features. The cheapest way to learn them is to run the operation once in a real context with a safety net and see what breaks. The cheapest way to *pass that learning forward* is to distill the resulting flow into a Claude-readable prompt afterward — fifteen minutes' work at the end of the session that turns one instance of hard-won local knowledge into a portable instruction for every future repo. **The extraction is the deliverable, not a side effect.** Most sessions produce code; some sessions produce a procedure. The second type is underrated because it doesn't feel like "I built something," but the compounding value per session is often higher: one extracted instruction applied across three future repos beats three copies of the same code change.
