---
name: runbook-bookshelf-clear-data-kills-app
description: "Android \"Clear data\" on Bookshelf prevents the app from launching afterwards — use uninstall + reinstall instead when the SQLite cache needs a wipe."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 357f34b8-2b9f-4268-b445-cf71fd75fda6
---

If the Bookshelf SQLite cache needs to be wiped (e.g. recovery from a half-populated state, schema corruption, future "Reset cache" affordance not yet built), **do NOT use Android Settings → Apps → Bookshelf → Storage → Clear data.** Confirmed 2026-05-14: Clear data left Bookshelf in a state where it would not launch afterwards. The fix was to uninstall the app and reinstall via fresh APK publish.

Use this instead: **uninstall + reinstall**. Slower (~1 min to republish), preserves the icon position in the launcher only momentarily, but reliably resets the app to clean-first-install state.

Root cause not yet diagnosed (Drew was unblocking, didn't capture logcat). Suspected culprits when next investigated:
- MSAL token-cache state expected by the auth SDK but absent vs corrupted (Clear data wipes app-private storage including MSAL's cache dir)
- `BuildInfo.DisplayString` initialisation reading something assembly-resource-shaped that Clear data invalidates
- A required private-storage directory the app expects to find pre-created but doesn't recreate on launch

Capture for the next investigation: trigger Clear data, attempt launch, run `adb logcat | findstr /i "AndroidRuntime FATAL Bookshelf"` while the launch is being attempted.

Long-term fix: in-app "Reset cache" button on MainPage that calls a `ICatalogCache.WipeAsync()` (blows away SQLite file + covers dir, leaves MSAL + Android permissions intact). Bypasses the Clear-data flow entirely. Tracked as a TODO row.
