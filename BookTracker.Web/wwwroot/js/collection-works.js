// JS helpers for the Add Book collection-mode "capture works" flow.
//
// Two responsibilities:
//
//   (a) Suppress the Enter key in collection-mode Title inputs so it
//       doesn't submit the surrounding form. Mirrors the chip-picker
//       arc's trap — Blazor's @onkeydown:preventDefault is a compile-
//       time constant; can't be conditional on the key. A document-
//       level keydown listener delegating to inputs marked with
//       data-collection-work-title preventDefaults Enter without
//       disturbing other keys (digits, letters, backspace) which still
//       reach the input normally. Blazor's element-level @onkeydown
//       still fires after — it handles the row-add / focus-next logic.
//
//   (b) Imperative focus on a specific row's Title input by index.
//       Called from Blazor after OnAfterRenderAsync sees a freshly-
//       added row so the DOM has had a chance to render the element.
//
// The suppression binds idempotently — first OnAfterRenderAsync after
// page load installs the listener; subsequent calls (re-renders, page
// revisits within the same SPA session) no-op.
window.collectionWorks = (function () {
    let suppressionBound = false;

    function bindEnterSuppression() {
        if (suppressionBound) return;
        suppressionBound = true;
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            const t = e.target;
            if (!t || typeof t.matches !== 'function') return;
            if (!t.matches('[data-collection-work-title]')) return;
            // preventDefault stops the form's default Enter-submits-form
            // behaviour. Other listeners (Blazor's @onkeydown) still
            // receive the event.
            e.preventDefault();
        });
    }

    function focusTitle(index) {
        const el = document.getElementById('collection-work-title-' + index);
        if (!el) return;
        el.focus();
        // Defensive: if the target had pre-filled text (shouldn't, for
        // a freshly-added empty row) put the cursor at the end rather
        // than selecting all and over-typing.
        if (typeof el.setSelectionRange === 'function') {
            const len = el.value ? el.value.length : 0;
            el.setSelectionRange(len, len);
        }
    }

    return { bindEnterSuppression, focusTitle };
})();
