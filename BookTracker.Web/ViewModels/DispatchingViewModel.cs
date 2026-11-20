using BookTracker.Application;
using BookTracker.Data;

namespace BookTracker.Web.ViewModels;

// Base for view models that dispatch commands. A DomainRuleException or
// NotFoundException is an expected outcome: it lands in Error with the message
// the domain wrote, for the component to show where the user acted. Anything
// else is a fault and propagates to the page-level error boundary.
public abstract class DispatchingViewModel
{
    public string? Error { get; set; }

    /// <summary>Runs the dispatch; false (with <see cref="Error"/> set) when the
    /// domain refused it.</summary>
    protected async Task<bool> DispatchAsync(Func<Task> dispatch)
    {
        Error = null;
        try
        {
            await dispatch();
            return true;
        }
        catch (Exception ex) when (ex is DomainRuleException or NotFoundException)
        {
            Error = ex.Message;
            return false;
        }
    }

    /// <summary>Runs the dispatch and returns its result; default (with
    /// <see cref="Error"/> set) when the domain refused it.</summary>
    protected async Task<T?> DispatchAsync<T>(Func<Task<T>> dispatch)
    {
        Error = null;
        try
        {
            return await dispatch();
        }
        catch (Exception ex) when (ex is DomainRuleException or NotFoundException)
        {
            Error = ex.Message;
            return default;
        }
    }
}
