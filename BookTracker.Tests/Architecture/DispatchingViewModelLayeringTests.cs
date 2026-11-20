using System.Reflection;
using BookTracker.Application;
using BookTracker.Web;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.Architecture;

/// <summary>
/// Keeps the failure path uniform: a ViewModel that takes <see cref="IDispatcher"/>
/// derives from <see cref="DispatchingViewModel"/>, so a refused command lands in
/// its <c>Error</c> for the component to show and anything else propagates to the
/// page-level error boundary.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DispatchingViewModelLayeringTests
{
    [Fact]
    public void EveryViewModelThatTakesTheDispatcherDerivesFromDispatchingViewModel()
    {
        var offenders = typeof(ProgramSetup).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IDispatcher))))
            .Where(t => !typeof(DispatchingViewModel).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(offenders.Count == 0,
            "ViewModels that take IDispatcher must derive from DispatchingViewModel and route " +
            "each dispatch through DispatchAsync:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }
}
