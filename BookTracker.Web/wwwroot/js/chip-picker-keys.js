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
            if (e.key === 'Enter' || e.key === ',') {
                e.preventDefault();
                const text = input.value;
                if (text && text.trim()) {
                    await dotnetRef.invokeMethodAsync('OnCommitKey', text);
                }
            }
        });
    }
};
