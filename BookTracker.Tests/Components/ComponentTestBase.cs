using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace BookTracker.Tests.Components;

/// <summary>
/// Shared bUnit harness for Razor-component tests. Wires up MudBlazor
/// services and permissive JS-interop so components that call
/// IJSRuntime in OnAfterRenderAsync (notably MudAuthorPicker via
/// chipPicker.suppressEnterAndComma) don't blow up the render.
///
/// bUnit renders components in-memory — no browser, no JS execution.
/// What it covers: parameter binding, event callback wiring, render
/// output, and the .NET side of any JS-interop boundary (the
/// [JSInvokable] methods can be called directly). What it doesn't:
/// the actual JS keydown handlers (those need slice (b) Playwright
/// when it lands), CSS / layout, real keyboard input simulation.
/// </summary>
public abstract class ComponentTestBase : BunitContext, IAsyncLifetime
{
    protected ComponentTestBase()
    {
        Services.AddMudServices(config =>
        {
            // Skip the popover-provider check — bUnit doesn't render the
            // popover container by default. Chip rendering is inline so
            // no popover is needed for the tests we care about.
            config.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Loose mode accepts any IJSRuntime call silently (returns default).
        // Chip-picker calls chipPicker.suppressEnterAndComma in
        // OnAfterRenderAsync; without Loose mode every render throws.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // Opt into xUnit's async lifecycle so disposal goes through
    // IAsyncDisposable.DisposeAsync() instead of the sync Dispose().
    // Required since bUnit 2.x + MudBlazor's KeyInterceptorService and
    // PointerEventsNoneService register as IAsyncDisposable-only,
    // which BunitServiceProvider.Dispose() (sync path) refuses to
    // dispose — "type only implements IAsyncDisposable. Use DisposeAsync
    // to dispose the container." Implementing IAsyncLifetime tells xUnit
    // to call our DisposeAsync, bypassing the broken sync path entirely.
    public Task InitializeAsync() => Task.CompletedTask;

    // `new` because BunitContext also declares DisposeAsync (returning
    // ValueTask via IAsyncDisposable); IAsyncLifetime requires Task.
    // Different return types, same name — C# resolves by name only, so
    // `new` acknowledges the intentional shadow.
    public new Task DisposeAsync() => ((IAsyncDisposable)this).DisposeAsync().AsTask();
}
