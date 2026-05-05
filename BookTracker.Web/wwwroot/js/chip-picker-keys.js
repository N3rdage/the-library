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

            // If the user has arrow-keyed to highlight a dropdown item, let
            // MudAutocomplete handle Enter natively — its own keydown handler
            // commits the highlighted pick via ValueChanged (which routes
            // through OnPickedAsync on the .NET side and adds the chip with
            // the FULL author name, not the partial typed text).
            //
            // aria-activedescendant is the standard combobox attribute and
            // MudAutocomplete sets it on the input only when arrow keys
            // focus a list item — empty/absent when the user is just typing
            // without navigating the dropdown. Belt-and-braces with a DOM
            // query for the selected-item class so the check works across
            // any MudBlazor versions that don't set aria-activedescendant.
            if (e.key === 'Enter') {
                const ariaActive = input.getAttribute('aria-activedescendant');
                const domHighlighted = document.querySelector('.mud-popover-open .mud-selected-item');
                if (ariaActive || domHighlighted) {
                    return;
                }
            }

            e.preventDefault();
            const text = input.value;
            if (text && text.trim()) {
                await dotnetRef.invokeMethodAsync('OnCommitKey', text);
            }
        });
    }
};
