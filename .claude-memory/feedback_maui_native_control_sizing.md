---
name: feedback_maui_native_control_sizing
description: A MAUI native-backed control (ZXing camera, maps, web/media views) sizes to its layout rectangle, not its HeightRequest — bound it with a fixed-size parent slot + clip.
metadata:
  type: feedback
---

A .NET MAUI control backed by a native Android surface — e.g. ZXing's `CameraBarcodeReaderView`, map views, web views, media players — does **not** reliably honour its own `HeightRequest`. The native preview surface sizes to fill and can z-order *over* sibling/parent content. Setting `HeightRequest` on the control, or on a wrapper around it, can do nothing.

**Why:** the inline barcode scanner on Bookshelf's FindPage (2026-06-16) filled the entire tab — the page header + search bar were drawn over — despite sitting in a wrapper with `HeightRequest="220"` in an `Auto` grid row. The native surface obeys the *layout slot it is handed*, not a requested size.

**How to apply:** bound it with the layout, not a request. Put the control in a parent whose rectangle is fixed — a `Grid` row with an explicit pixel `Height` (name the `RowDefinition` and toggle its `Height` 0↔N in code to collapse/expand, rather than relying on the child's `IsVisible` + `HeightRequest`), plus `IsClippedToBounds="True"` on the container. The explicit row rect is what the native surface fills and clips to. Verify on-device — emulators and the layout previewer don't always reproduce native-surface behaviour. Generalises to any native-backed view that won't stay in its box. See [[retro_bookshelf_redesign_arc]].
