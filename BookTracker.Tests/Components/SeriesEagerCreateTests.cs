using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Web.Components.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// Unit tests for the shared series eager-create (the dispatch + swallow-fault
/// block shared by the BookAdd manual/accept paths and BulkAdd per-row accept).
/// Mirrors PublisherEagerCreateTests; series has no membership check (callers gate).
/// </summary>
public class SeriesEagerCreateTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    [Fact]
    public async Task DispatchesEnsureSeries_ReturnsId()
    {
        _dispatcher.Send(Arg.Any<EnsureSeries>(), Arg.Any<CancellationToken>()).Returns((int?)42);

        var id = await SeriesEagerCreate.EnsureAsync("Mistborn", _dispatcher, NullLogger.Instance);

        Assert.Equal(42, id);
        await _dispatcher.Received(1).Send(
            Arg.Is<EnsureSeries>(c => c.Name == "Mistborn"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchThrows_SwallowedReturnsNull()
    {
        // Best-effort: a faulting dispatch must not surface — the save's
        // SeriesResolver net guarantees the row.
        _dispatcher.Send(Arg.Any<EnsureSeries>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new InvalidOperationException("transient")));

        var id = await SeriesEagerCreate.EnsureAsync("Mistborn", _dispatcher, NullLogger.Instance);

        Assert.Null(id);
    }
}
