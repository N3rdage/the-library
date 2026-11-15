using BookTracker.Data;
using BookTracker.Web.Api;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BookTracker.Tests.Api;

// Guards the once-per-process behaviour of /warmup (LIB-013): on an
// instance that has already warmed its SQL path the handler must answer
// without asking the DbContext factory for a context. The cold path (a real
// query against the Testcontainer) is covered by WarmupEndpointTests.
[Trait("Category", TestCategories.Unit)]
public class WarmupEndpointsTests
{
    [Fact]
    public async Task WarmInstance_AnswersWithoutOpeningADbContext()
    {
        var factory = Substitute.For<IDbContextFactory<BookTrackerDbContext>>();
        var state = new WarmupState();
        state.MarkWarm();

        var result = await WarmupEndpoints.HandleAsync(state, factory, CancellationToken.None);

        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("warm", content.ResponseContent);
    }

    [Fact]
    public void State_StartsColdAndStaysWarmOnceMarked()
    {
        var state = new WarmupState();

        Assert.False(state.IsWarm);
        state.MarkWarm();
        Assert.True(state.IsWarm);
        state.MarkWarm();
        Assert.True(state.IsWarm);
    }
}
