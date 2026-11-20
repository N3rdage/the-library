using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BookTracker.Web.Components.Layout;

/// <summary>
/// The page-level boundary: a fault (anything that is not a domain rule or
/// not-found outcome, which the dispatching view models show where the user
/// acted) is logged with a trace id and replaces the page with the error
/// content, leaving the circuit alive. <see cref="ErrorBoundaryBase.Recover"/>
/// puts the page back.
/// </summary>
public sealed class PageErrorBoundary : ErrorBoundaryBase
{
    [Inject] private ILogger<PageErrorBoundary> Logger { get; set; } = default!;

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    /// <summary>The id logged with the current fault, for the user to quote.</summary>
    public string? TraceId { get; private set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (CurrentException is { } exception)
            builder.AddContent(0, ErrorContent, exception);
        else
            builder.AddContent(1, ChildContent);
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        // A circuit has no HttpContext and often no ambient Activity, so a
        // generated id keeps the logged and displayed ids the same.
        TraceId = Activity.Current?.TraceId.ToString()
            ?? HttpContext?.TraceIdentifier
            ?? Guid.NewGuid().ToString("N");
        Logger.LogError(exception, "Unhandled fault on a page. Trace ID {TraceId}", TraceId);
        return Task.CompletedTask;
    }
}
