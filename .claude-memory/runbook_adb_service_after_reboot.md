---
name: runbook-adb-service-after-reboot
description: "If Bookshelf MAUI build/publish/deploy fails after a laptop reboot — especially with cryptic build summaries and warnings dominating the output — check adb first. The Android Debug Bridge daemon doesn't auto-start on Drew's Windows laptop."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 357f34b8-2b9f-4268-b445-cf71fd75fda6
---

When a Bookshelf MAUI build / publish / deploy fails after a laptop reboot, check that `adb` (Android Debug Bridge) is running before chasing apparent build errors.

**Why:** Drew's Windows laptop doesn't auto-start the adb daemon on boot. Without adb, dotnet's Android publish/deploy steps fail with summary lines that don't obviously point at adb — on 2026-05-14 the failure surfaced as `BookTracker.Mobile net10.0-android failed with 1 error(s) and 6 warning(s)` with XA0141 (SkiaSharp 16KB page) warnings dominating the visible snippet. The actual blocker was adb not running. Several minutes spent considering a SkiaSharp 2.x → 3.x upgrade before Drew identified the root cause.

**How to apply:** When a MAUI build/publish fails on a fresh boot (or after long sleep), run `adb start-server` from a terminal before diagnosing the build output. Verify with `adb devices` — should list "List of devices attached" without errors. If adb output looks healthy, the build failure is a real code issue; if adb itself errors, that's the bug.
