// JS helpers for the Add Book collection-mode "capture works" flow.
//
// Two responsibilities:
//
//   (a) Suppress the Enter key on each collection-mode Title input so it
//       doesn't submit the surrounding form. Mirrors the chip-picker
//       arc's trap — Blazor's @onkeydown:preventDefault is a compile-
//       time constant; can't be conditional on the key. A document-
//       level keydown listener delegating to inputs inside an element
//       marked with data-collection-work-title preventDefaults Enter
//       without disturbing other keys. When MudAutocomplete has a
//       highlighted dropdown suggestion (aria-activedescendant or the
//       DOM-highlighted .mud-selected-item) we defer — MudAutocomplete's
//       own listener will commit the pick via ValueChanged. Otherwise
//       we invoke the page's [JSInvokable] OnCollectionTitleEnter to
//       run the "add new row / focus next row" logic.
//
//   (b) Imperative focus on a specific row's Title input by index.
//       Called from Blazor after OnAfterRenderAsync sees a freshly-
//       added row so the DOM has had a chance to render the element.
//
// The suppression binds idempotently — first OnAfterRenderAsync after
// page load installs the listener; subsequent calls (re-renders, page
// revisits within the same SPA session) no-op. The DotNetObjectReference
// is stashed for the lifetime of the page; replaced if bindEnterSuppression
// is called again with a new ref (e.g. after a circuit reconnect).
window.collectionWorks = (function () {
    let suppressionBound = false;
    let pageDotnetRef = null;

    function bindEnterSuppression(dotnetRef) {
        // Always update the ref — a stale one from a previous circuit
        // would hand the keydown to a disposed Blazor object.
        pageDotnetRef = dotnetRef;
        if (suppressionBound) return;
        suppressionBound = true;
        document.addEventListener('keydown', async function (e) {
            if (e.key !== 'Enter') return;
            const t = e.target;
            if (!t || typeof t.matches !== 'function') return;

            // Match either the wrapper itself or any input/element inside one.
            // MudAutocomplete renders the input deep inside its own DOM, so
            // we can't put the data-attribute directly on the input.
            const wrapper = t.closest === undefined ? null : t.closest('[data-collection-work-title]');
            if (!wrapper) return;

            // Always preventDefault Enter — keeps the surrounding form from
            // submitting. Other listeners (MudAutocomplete's own) still fire.
            e.preventDefault();

            // Defer to MudAutocomplete when a dropdown suggestion is
            // highlighted — its ValueChanged will flip the row to attach-
            // existing mode. Same shape as the chip-picker arc.
            const input = t.tagName === 'INPUT' ? t : wrapper.querySelector('input');
            const ariaActive = input ? input.getAttribute('aria-activedescendant') : null;
            const domHighlighted = document.querySelector('.mud-popover-open .mud-selected-item');
            if (ariaActive || domHighlighted) {
                return;
            }

            const indexStr = wrapper.getAttribute('data-collection-work-title');
            const index = parseInt(indexStr, 10);
            if (isNaN(index)) return;

            if (pageDotnetRef) {
                await pageDotnetRef.invokeMethodAsync('OnCollectionTitleEnter', index);
            }
        });
    }

    function focusTitle(index) {
        const wrapper = document.querySelector('[data-collection-work-title="' + index + '"]');
        if (!wrapper) return;
        const input = wrapper.querySelector('input');
        if (!input) return;
        input.focus();
        if (typeof input.setSelectionRange === 'function') {
            const len = input.value ? input.value.length : 0;
            input.setSelectionRange(len, len);
        }
    }

    return { bindEnterSuppression, focusTitle };
})();
