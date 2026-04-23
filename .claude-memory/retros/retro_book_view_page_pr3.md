---
name: Book View page — PR 3 (modal edits for Book + Work)
description: Third PR of the book-detail arc. Swaps the "Edit all details" escape hatch for two MudDialog-backed edit surfaces reachable without leaving the View page — title/category/cover for Book, title/subtitle/author/date/series for Work. Validates the per-modal VM pattern set up in PR 2's retro, and handles the MudListItem click-propagation gotcha.
type: project
---

## Shipped

PR #98 — two MudBlazor dialogs accessible from `/books/{id}`:
- **Edit book details** — title, category, cover URL. Three fields, tight.
- **Edit work** — title, subtitle, author (typeahead over existing Authors + create-on-save via `AuthorResolver`), first-published date (`PartialDateParser` free-text), series + order.

Button placement: single-Work books get "Edit book details" + "Edit work" side-by-side in the header action row. Multi-Work compendiums get "Edit book details" in the header and a per-Work "Edit work" button inside each expanded row in the Works list.

Architecture: each modal owns its own VM (`BookEditDialogViewModel` ~45 lines, `WorkEditDialogViewModel` ~85 lines), keeping `BookDetailViewModel` at 280 lines — unchanged from PR 2. The rule from PR 2's retro ("modals >50 lines get their own VM") held up well.

Deliberately deferred: genres stay on `/edit` until a hierarchical MudBlazor picker gets its own PR. "Add other works" / "Attach existing Work" for compendium-building also stay on `/edit`. Editions and copies are PR 4.

## Surprise

- **MudListItem's OnClick captures every click inside it — stopPropagation is not optional.** Multi-Work compendium rows are built on `MudListItem T="int" OnClick="@(() => ToggleWork(w.Id))"`. Adding an "Edit work" button inside the expanded area meant clicking the button both toggled the expansion (collapsing the row) AND opened the dialog. The fix: wrap the entire expanded content in `<div @onclick:stopPropagation="true">`. Not a MudBlazor-specific quirk — this is how DOM click events work — but it's the kind of bug that you don't catch until the first time you try to click a button inside a clickable container, and then it's immediate. Rule: any clickable-parent + interactive-child pairing needs deliberate propagation plumbing at design time, not as a fix-later.
- **`DialogParameters<T> { { x => x.BookId, VM.Book.Id } }` is nicer than I expected.** The expression-based parameter-passing syntax gives type-safe binding — if I rename `BookId` on the dialog component, the Razor compiler catches the call site. I'd previously expected `new DialogParameters().Add("BookId", id)` string-keyed nonsense. Small QoL wins like this are why a mature component library beats hand-rolled: the ergonomic decisions have already been made for you.
- **MudAutocomplete's `CoerceValue` + `CoerceText` turn "pick existing or create new" into a two-flag toggle.** My mental model was "autocompletes are picker widgets; free text is a textbox; they fight each other." MudAutocomplete collapses this: `CoerceValue="true"` keeps the free-typed text as the value even when no match is selected; `CoerceText="true"` keeps the text display synced to the value. Combined with a server-side `SearchFunc`, the whole "find-or-create with typeahead" pattern ended up being ~6 attributes on one component. Going to reuse this pattern heavily — it's the right shape for anywhere the user input maps to an entity (Author, Publisher, Tag, Series, Genre).
- **Test expectation vs code behaviour: I asserted the wrong date format.** `PartialDateParser.Format` returns "12 Nov 1987" for Day precision, not "1987-11-12". I'd assumed ISO from context; the function formats for humans. Test caught it first run with a clear Expected/Actual diff. Lesson (again): read the formatter before asserting its output — same lesson as PR 1's edition-copy-count arithmetic bug. A recurring class of self-inflicted test failure is "I wrote what I imagined, not what the code does."
- **Renaming the old "Edit all details" button was part of the PR's UX work, not polish.** Before PR 3, "Edit all details" was the only way to edit anything beyond rating/status/notes/tags — so "all details" was literally true. After PR 3, the modals cover Book + Work fields. The escape-hatch button now exists only for genres + compendium building, so "all details" was misleading. Renamed it to "Full edit page" with a text-style (not outlined) button so it visually recedes. Reframing button labels as features land is ongoing maintenance worth doing in the same PR — if the label was accurate yesterday and isn't today, that's a bug in the PR that changed yesterday's assumptions.

## Lesson

- **Per-modal VMs keep the page VM on a diet.** `BookDetailViewModel` didn't grow at all in PR 3. It orchestrates ("open this dialog with these params, re-initialise when it closes") but doesn't carry modal edit state, validation, or save methods. If I'd put `SaveBookDetailsAsync()` + `SaveWorkAsync()` directly on `BookDetailViewModel`, the page VM would already be past 400 lines and mixing display-shape responsibilities with dialog edit state. The rule ("modals >50 lines get their own VM") wasn't just arbitrary — it's the point past which conflating the two concerns starts costing real mental overhead.
- **The MudBlazor dialog idiom is: `DialogParameters<T>` + `DialogOptions` + `ShowAsync<T>` + `await dialog.Result`.** Standard shape; once internalised, a new dialog is ~20 lines of razor + ~10 lines of handler. Pattern:
  1. Dialog component uses `[CascadingParameter] IMudDialogInstance MudDialog`.
  2. `[Parameter]` for each input the dialog needs (BookId, WorkId, etc.).
  3. `OnInitializedAsync` loads via the dialog VM.
  4. On Save: `await VM.SaveAsync()` → `MudDialog.Close(DialogResult.Ok(true))`.
  5. Parent awaits `dialog.Result`, checks `!Canceled`, refreshes + snackbars.
  Worth carving this out as a project-level convention so PR 4's Edition/Copy modals follow the same shape without re-deciding.
- **"Find-or-create with typeahead" is a generalisable pattern across the codebase.** Author is the first MudAutocomplete-driven find-or-create surface. Coming up: similar flow for Publisher on Add Edition, likely for Series on Add Work, conceivably for Tag (PR 2 already does it via a simpler flat-list autocomplete). The shape — `MudAutocomplete<string>` + server-side `SearchFunc` + `AuthorResolver`-style `FindOrCreate` at save — is the right abstraction, and rebuilds cleanly per entity. If we added a fourth instance, extracting a generic wrapper would be warranted; for three, keep them as explicit instances that share the mental model rather than the code.
- **Propagation plumbing at design time, not fix-later.** Any time a clickable parent contains interactive children (MudListItem with a button inside, a row-click handler with a nested select, a card click handler with a form), flag it at design time and pick the resolution: either stopPropagation on the inner interactive region, or factor the row so only the non-interactive part is clickable. Not catching this in design reliably produces a confusing first-click bug; the fix is cheap but the bug is 100% on-brand for UI devs. Add it to the design-review checklist for new clickable-container UIs.

## Quotable

"Per-modal VMs keep the page VM on a diet" generalises past Blazor. Any UI with a primary page-state object that accumulates modal edit concerns will, over time, become The Everything ViewModel — a 900-line class that does display, routing, save state for four unrelated forms, and tag management. The fix is simple and the threshold is clear: if a modal's edit logic is more than ~50 lines, it gets its own VM. The page VM orchestrates the opening and closing; the modal VM owns the edit. This isn't a Blazor rule or a MudBlazor rule — it's a separation-of-concerns rule with a specific trigger. The specific trigger is what makes it useful as guidance: "write modals small" is too soft; ">50 lines → split" is actionable.
