// Suppresses Enter and comma at the keydown layer so they don't trigger
// the surrounding form's submit (Enter) or get inserted into the input
// (comma), and immediately invokes .NET to commit the typed text as a
// chip. Used by MudAuthorPicker.
//
// Why JS not pure Blazor: Blazor's @onkeydown:preventDefault directive
// can't be conditional on the key value at runtime (it's evaluated as a
// static bool when the listener is registered). And reading the
// MudAutocomplete's internal Text from the .NET side at keydown time was
// unreliable in practice (came back stale or empty), so the JS handler
// reads input.value directly at the exact moment the key fires.
//
// Cleanup: when the host element is removed from the DOM the listener is
// garbage-collected with it. The DotNetObjectReference passed in is
// disposed by the picker's IDisposable.Dispose.
window.chipPicker = {
    suppressEnterAndComma(container, dotnetRef) {
        if (!container) return;
        const input = container.querySelector('input');
        if (!input) return;
        input.addEventListener('keydown', async (e) => {
            if (e.key !== 'Enter' && e.key !== ',') return;

            // ALWAYS preventDefault Enter/comma — keeps the surrounding form
            // from submitting (Enter) and the comma from being inserted into
            // the input. preventDefault only stops the browser's default
            // action; other keydown listeners (including MudAutocomplete's
            // own) still fire normally.
            e.preventDefault();

            // For Enter specifically: if a dropdown item is keyboard-highlighted,
            // MudAutocomplete's own keydown handler will commit the pick via
            // ValueChanged → OnPickedAsync (chip added with the FULL author
            // name). Don't ALSO invoke our typed-text commit in that case —
            // doing so would double-add or replace the proper pick with the
            // partial typed text. Comma is unconditional because no dropdown
            // navigation is expected for that key.
            if (e.key === 'Enter') {
                const ariaActive = input.getAttribute('aria-activedescendant');
                const domHighlighted = document.querySelector('.mud-popover-open .mud-selected-item');
                if (ariaActive || domHighlighted) {
                    return;
                }
            }

            const text = input.value;
            if (text && text.trim()) {
                await dotnetRef.invokeMethodAsync('OnCommitKey', text);
            }
        });
    }
};
