// Suppresses Enter and comma at the keydown layer so they don't trigger
// the surrounding form's submit (Enter) or get inserted into the input
// (comma). Used by MudAuthorPicker — the .NET-side OnKeyDown handler
// still fires on the same keys to commit the typed text as a chip; this
// JS just preventDefaults so the form/input doesn't ALSO see them.
//
// Blazor's @onkeydown:preventDefault directive can't be conditional on
// the key value at runtime (it's a static attribute), so the only
// reliable path is a JS-side keydown listener registered via interop.
//
// Cleanup: when the host element is removed from the DOM the listener
// is garbage-collected with it. No explicit unsubscribe needed for
// the dialog/page lifetimes this picker lives in.
window.chipPicker = {
    suppressEnterAndComma(container) {
        if (!container) return;
        const input = container.querySelector('input');
        if (!input) return;
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ',') {
                e.preventDefault();
            }
        });
    }
};
