using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Web.Components.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// Unit tests for the shared publisher eager-create helper (TD-15a). Both
/// publisher fields — EditionCopyForm (/books/add) and EditionFormDialog
/// (book-detail edit) — delegate their skip-existing / dispatch-new /
/// swallow-fault branch here, so these cover the regression-prone logic for both
/// without rendering either. No DB / bUnit needed: the dispatcher is substituted.
/// </summary>
public class PublisherEagerCreateTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private Task<(int Id, string Name)?> Run(string? value, params string[] existing)
        => PublisherEagerCreate.CreateIfNewAsync(value, existing, _dispatcher, NullLogger.Instance);

    [Fact]
    public async Task NewName_DispatchesAndReturnsCreated()
    {
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>()).Returns((int?)42);

        var result = await Run("Gollancz");

        Assert.NotNull(result);
        Assert.Equal(42, result.Value.Id);
        Assert.Equal("Gollancz", result.Value.Name);
        await _dispatcher.Received(1).Send(
            Arg.Is<CreatePublisher>(c => c.Name == "Gollancz"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewName_IsTrimmedBeforeDispatch()
    {
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>()).Returns((int?)7);

        var result = await Run("  Gollancz  ");

        Assert.NotNull(result);
        Assert.Equal("Gollancz", result.Value.Name);
        await _dispatcher.Received(1).Send(
            Arg.Is<CreatePublisher>(c => c.Name == "Gollancz"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingName_SkipsDispatch()
    {
        // Case-insensitive membership — committing an existing pick (even
        // mis-cased) is not a create.
        var result = await Run("penguin books", "Penguin Books");

        Assert.Null(result);
        await _dispatcher.DidNotReceive().Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_SkipsDispatch(string? value)
    {
        var result = await Run(value);

        Assert.Null(result);
        await _dispatcher.DidNotReceive().Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchThrows_SwallowedReturnsNull()
    {
        // Best-effort: a faulting dispatch must not surface — the save's
        // PublisherResolver net guarantees the row.
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new InvalidOperationException("transient")));

        var result = await Run("Gollancz");

        Assert.Null(result);
    }
}
