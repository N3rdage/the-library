---
name: Blazor dialog VM lifetime trap
description: Transient ViewModels @injected inside a MudDialog get a fresh instance, not the page's — pass the page's VM via DialogParameters when shared state is needed.
type: feedback
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
When a MudBlazor dialog needs the page's initialised state (e.g. `VM.Book`), do NOT `@inject` the same VM type inside the dialog. BookTracker registers ViewModels as Transient, so `@inject` hands the dialog a brand-new instance whose state (Book, snapshot, etc.) is empty — the dialog calls VM methods that silently no-op or return empty.

**Why:** caught in PR 4 of the Add/View/Edit polish arc (2026-05-12). The "Add existing work" dialog @injected `BookDetailViewModel` and called `VM.SearchAttachableWorksAsync` — which short-circuits when `Book is null`. The full edit page worked because it has a different VM and didn't hit this code path. Drew's repro: "in the view page I get no works found for a work that the full edit page finds."

**How to apply:** when a dialog needs the page's VM state, follow the EditionCoverUploadDialog pattern — declare a `[Parameter, EditorRequired] public TheVM VM { get; set; }` on the dialog and pass it through from the page via `DialogParameters<TheDialog> { { x => x.VM, VM } }`. Dialogs that only need stateless services (IBookLookupService, IWorkSearchService, IDbContextFactory) can still `@inject` them — the rule is specifically about VMs that hold initialised page state.
