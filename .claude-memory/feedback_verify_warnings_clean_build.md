---
name: feedback_verify_warnings_clean_build
description: "Don't claim \"0 warnings\" from an incremental build — verify with --no-incremental (or a full rebuild)."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 90df9d68-a582-4a86-895a-e1efab27cd96
---

Incremental `dotnet build` only recompiles assemblies whose inputs changed, so warnings in untouched projects — and even in files I just edited once their assembly is cached on a second pass — do **not** reappear. Reporting "0 warnings" off an incremental build is misleading.

**Why:** Drew ran a full rebuild and surfaced both pre-existing warnings (Web `RZ10012`/`MUD0002`, Tests `CS0618`) and — more importantly — 5 **new** CS0618s my own motion code had introduced (`FadeTo`/`TranslateTo`/`ScaleTo` are obsolete in net10 MAUI; use the `*Async` variants). My incremental builds had shown "0 warnings" the whole time.

**How to apply:** before asserting a change is warning-clean, run `dotnet build <proj> -f <tfm> --no-incremental` (or `-t:Rebuild`) and grep the output for `: warning`. Distinguish *my* warnings from pre-existing/third-party noise (e.g. the Mobile project's XA0141 16KB-page-size warnings come from the ZXing camera-core + sqlite native NuGets, not our code — those are expected and not actionable). Relates to [[feedback_hot_reload_commit_gap]] — both are "looks done locally but isn't really verified" traps.
