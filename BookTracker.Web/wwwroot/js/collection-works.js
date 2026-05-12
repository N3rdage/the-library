// JS helpers for the Add Book collection-mode "capture works" flow.
//
// Three responsibilities:
//
//   (a) Stop the surrounding form from submitting on Enter inside a
//       collection-mode Title input. Mirrors the chip-picker arc's
//       trap — Blazor's @onkeydown:preventDefault is a compile-time
//       constant and can't be conditional on the key.
//
//   (b) Decide what Enter MEANS on a Title input. MudAutocomplete
//       auto-highlights the first dropdown match as soon as the
//       dropdown opens, so a naive "if highlighted, defer to
//       MudAutocomplete" check would pick "Condor" whenever the user
//       typed "Con" and pressed Enter — even if they intended to
//       capture "Con..." as a new Work. Drew's repro. Fix: track
//       whether the user has explicitly arrow-keyed since the last
//       text edit. Only THEN does Enter defer to MudAutocomplete; an
//       Enter on a never-navigated input goes to our row-add handler
//       with the typed text intact.
//
//       Implementation runs at the CAPTURE phase so we fire BEFORE
//       MudAutocomplete's own keydown listener. When we decide the
//       Enter is "free text", we stopImmediatePropagation — that
//       blocks MudAutocomplete from committing its auto-highlighted
//       item. When we decide it's "navigated", we let the event flow
//       through and MudAutocomplete commits the pick (→ ValueChanged
//       → OnExistingWorkPickedAsync).
//
//   (c) Imperative focus on a specific row's Title input by index.
//       Called from Blazor after OnAfterRenderAsync sees a freshly-
//       added row so the DOM has had a chance to render the element.
//
// Idempotent binding — first OnAfterRenderAsync after page load
// installs the listener; subsequent calls (re-renders, page revisits
// within the same SPA session) just refresh the DotNetObjectReference.
window.collectionWorks = (function () {
    let listenerBound = false;
    let pageDotnetRef = null;
    // WeakSet of input elements where the user has pressed Down/Up since
    // the last text edit. WeakSet so detached input elements (removed
    // rows, mode flips to attach) get garbage-collected without a leak.
    const navigatedInputs = new WeakSet();

    function isPrintableKey(key) {
        // Single-char keys are typing; Backspace/Delete also count as
        // "the user is editing the text" and should reset navigation.
        return (key && key.length === 1) || key === 'Backspace' || key === 'Delete';
    }

    function bindEnterSuppression(dotnetRef) {
        // Only update the page ref when a real one is passed. The
        // AddWorkDialog on the View page reuses this listener for its
        // single title autocomplete (wrapper with data-collection-work-title="-1")
        // and calls bindEnterSuppression(null) — we want the suppression
        // behaviour without clobbering Add Book's real ref if it has
        // already registered, and without invoking anything when there's
        // no row-add semantics to fire.
        if (dotnetRef) pageDotnetRef = dotnetRef;
        if (listenerBound) return;
        listenerBound = true;

        document.addEventListener('keydown', async function (e) {
            const t = e.target;
            if (!t || typeof t.matches !== 'function') return;

            const wrapper = t.closest === undefined ? null : t.closest('[data-collection-work-title]');
            if (!wrapper) return;

            const input = t.tagName === 'INPUT' ? t : wrapper.querySelector('input');
            if (!input) return;

            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                navigatedInputs.add(input);
                return;
            }

            if (isPrintableKey(e.key)) {
                // User edited the text — reset navigation. A fresh search
                // will re-populate the dropdown with first-item auto-
                // highlighted; we treat that as "not yet navigated" again.
                navigatedInputs.delete(input);
                return;
            }

            if (e.key === 'Escape') {
                // Clear navigation flag so a subsequent Enter is "free
                // text". (MudAutocomplete handles closing the dropdown
                // itself — we don't preventDefault here.)
                navigatedInputs.delete(input);
                return;
            }

            if (e.key !== 'Enter') return;

            // preventDefault unconditionally — Enter inside a form must
            // never submit it.
            e.preventDefault();

            if (navigatedInputs.has(input)) {
                // User explicitly arrow-keyed to a suggestion — let
                // MudAutocomplete's own listener (which fires after this
                // capture-phase one, at the input/bubble phase) commit
                // the pick. ValueChanged → OnExistingWorkPickedAsync
                // flips the row to attach mode. Clear the flag so the
                // next typing cycle starts fresh.
                navigatedInputs.delete(input);
                return;
            }

            // Free-text Enter: block MudAutocomplete's auto-pick of the
            // first-highlighted item, then invoke the page's add-row /
            // focus-next handler with the typed text intact.
            //
            // Known trade-off: the popover stays visible after this
            // Enter because stopImmediatePropagation prevents
            // MudAutocomplete from learning about the keypress. The
            // user dismisses it with Esc (universal "close popup"
            // affordance). Three close-it-for-them attempts failed:
            // input.blur() doesn't cascade to a portaled popover;
            // synthetic Esc keydown gets filtered out by Blazor's
            // event delegation (isTrusted=false); directly removing
            // the .mud-popover-open class drifted MudAutocomplete's
            // internal state vs. the DOM and didn't actually hide
            // the popover. Living with the papercut is cheaper than
            // a fragile fix.
            e.stopImmediatePropagation();

            const indexStr = wrapper.getAttribute('data-collection-work-title');
            const index = parseInt(indexStr, 10);
            if (isNaN(index) || index < 0) {
                // Negative index = dialog usage (AddWorkDialog) where there'\''s
                // no row-add semantics to fire — suppression alone is enough.
                return;
            }

            if (pageDotnetRef) {
                try {
                    await pageDotnetRef.invokeMethodAsync('OnCollectionTitleEnter', index);
                } catch (err) {
                    // pageDotnetRef may be a disposed Add Book page (user
                    // navigated away). Silently ignore — the suppression
                    // itself already did its job.
                }
            }
        }, true /* capture phase — fire BEFORE input-level listeners */);
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
