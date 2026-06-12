---
name: blazor-virtualize-needs-using
description: <Virtualize> needs an explicit @using on each page; without it Razor silently treats it as raw HTML and you get a misleading CS0103 on the context variable.
metadata:
  type: project
---
`<Virtualize>` lives in `Microsoft.AspNetCore.Components.Web.Virtualization`, which is **not** in this project's `_Imports.razor`. Every page that uses it must add `@using Microsoft.AspNetCore.Components.Web.Virtualization` itself (e.g. `Authors/Index.razor` does).

**Why this matters:** without the using, Razor doesn't resolve `<Virtualize>` to the component — it treats it as a **raw HTML element**. The failure is silent and misleading:
- `<ItemContent>` is not recognised as a template parameter, so no context variable is generated → `CS0103: The name 'context'/'<your-context>' does not exist` on every reference inside the row.
- `Items="..."` does **not** error (it's just an HTML attribute string), which sends you hunting in the wrong place (Context name, generic inference, `TItem`, nested-type resolution — all red herrings).

**How to apply:** when a `<Virtualize>` (or any component) throws `CS0103` on its context variable while its other attributes compile fine, the component isn't resolving — check the page has the right `@using` before touching anything else. Confirmed 2026-06-12 building the Library group-list virtualization (PR2 of the Library nav rework); ~12 build cycles lost before spotting the missing line by diffing against `Authors/Index.razor`. Relates to [[mudblazor-menu-popover-gotchas]].
