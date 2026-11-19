using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Services.Covers;

// CoverMirrorQueue against real SQL: the due queries must translate to one
// statement each and honour the state rows, and the state writes must keep
// one row per target with the backoff arithmetic applied.
[Trait("Category", TestCategories.Integration)]
public class CoverMirrorQueueTests
{
    private const string CoverUrl = "https://covers.example.org/b/isbn/9780553293357-L.jpg";
    private const string OtherUrl = "https://covers.example.org/b/isbn/9780553293357-M.jpg";

    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task EditionWithoutState_IsDue()
    {
        var (_, editionId) = await SeedAsync();

        Assert.Contains(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task EditionWithBlankCover_IsNotDue()
    {
        var (_, editionId) = await SeedAsync(editionCoverUrl: "");

        Assert.DoesNotContain(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task TransientStateBeforeNextAttempt_IsNotDue()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now.AddMinutes(1));

        Assert.DoesNotContain(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task TransientStateAtNextAttempt_IsDue()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);

        Assert.Contains(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task TransientStateAfterNextAttempt_IsDue()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now.AddMinutes(-1));

        Assert.Contains(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task PermanentStateWithMatchingUrl_IsNeverDue()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, CoverUrl, CoverMirrorOutcome.PermanentRefusal, Now.AddMinutes(-1));

        Assert.DoesNotContain(editionId, await DueEditionIdsAsync(Now.AddDays(30)));
    }

    [Fact]
    public async Task PermanentStateWithDifferentUrl_IsDue()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, OtherUrl, CoverMirrorOutcome.PermanentRefusal, Now.AddHours(1));

        Assert.Contains(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task BookState_DoesNotHoldBackTheEditionWithTheSameId()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Book, editionId, CoverUrl, CoverMirrorOutcome.PermanentRefusal, Now.AddHours(1));

        Assert.Contains(editionId, await DueEditionIdsAsync(Now));
    }

    [Fact]
    public async Task BookWithPermanentState_IsNotDue_AndWithoutState_IsDue()
    {
        var (heldBookId, _) = await SeedAsync(bookCoverUrl: CoverUrl);
        var (dueBookId, _) = await SeedAsync(bookCoverUrl: CoverUrl, isbn: "9780553293358");
        await AddStateAsync(CoverMirrorTarget.Book, heldBookId, CoverUrl, CoverMirrorOutcome.PermanentRefusal, Now.AddHours(1));

        await using var db = _factory.CreateDbContext();
        var due = await CoverMirrorQueue.DueBooksAsync(db, Now, CancellationToken.None);

        Assert.DoesNotContain(due, c => c.Id == heldBookId);
        Assert.Contains(due, c => c.Id == dueBookId && c.Url == CoverUrl);
    }

    [Fact]
    public async Task RecordFailure_TwiceWithSameUrl_DoublesTheBackoff()
    {
        var (_, editionId) = await SeedAsync();

        await using (var db = _factory.CreateDbContext())
        {
            await CoverMirrorQueue.RecordFailureAsync(db, CoverMirrorTarget.Edition, editionId, CoverUrl,
                CoverMirrorOutcome.TransientFailure, Now.AddMinutes(-1), CancellationToken.None);
        }
        await using (var db = _factory.CreateDbContext())
        {
            await CoverMirrorQueue.RecordFailureAsync(db, CoverMirrorTarget.Edition, editionId, CoverUrl,
                CoverMirrorOutcome.TransientFailure, Now, CancellationToken.None);
        }

        var state = await SingleStateAsync(CoverMirrorTarget.Edition, editionId);
        Assert.Equal(2, state.Attempts);
        Assert.Equal(CoverUrl, state.Url);
        Assert.Equal(Now, state.LastAttemptUtc);
        Assert.Equal(Now.AddMinutes(2), state.NextAttemptUtc);
        Assert.Equal(DateTimeKind.Utc, state.NextAttemptUtc.Kind);
        Assert.Equal(CoverMirrorOutcome.TransientFailure, state.LastOutcome);
    }

    [Fact]
    public async Task RecordFailure_WithChangedUrl_ResetsAttempts()
    {
        var (_, editionId) = await SeedAsync();

        await using (var db = _factory.CreateDbContext())
        {
            await CoverMirrorQueue.RecordFailureAsync(db, CoverMirrorTarget.Edition, editionId, CoverUrl,
                CoverMirrorOutcome.TransientFailure, Now.AddMinutes(-5), CancellationToken.None);
            await CoverMirrorQueue.RecordFailureAsync(db, CoverMirrorTarget.Edition, editionId, CoverUrl,
                CoverMirrorOutcome.TransientFailure, Now.AddMinutes(-3), CancellationToken.None);
        }
        await using (var db = _factory.CreateDbContext())
        {
            await CoverMirrorQueue.RecordFailureAsync(db, CoverMirrorTarget.Edition, editionId, OtherUrl,
                CoverMirrorOutcome.PermanentRefusal, Now, CancellationToken.None);
        }

        var state = await SingleStateAsync(CoverMirrorTarget.Edition, editionId);
        Assert.Equal(1, state.Attempts);
        Assert.Equal(OtherUrl, state.Url);
        Assert.Equal(Now.AddMinutes(1), state.NextAttemptUtc);
        Assert.Equal(CoverMirrorOutcome.PermanentRefusal, state.LastOutcome);
    }

    [Fact]
    public async Task Clear_RemovesTheStateRow()
    {
        var (_, editionId) = await SeedAsync();
        await AddStateAsync(CoverMirrorTarget.Edition, editionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);

        await using (var db = _factory.CreateDbContext())
        {
            await CoverMirrorQueue.ClearAsync(db, CoverMirrorTarget.Edition, editionId, CancellationToken.None);
        }

        await using var check = _factory.CreateDbContext();
        Assert.False(await check.CoverMirrorStates.AnyAsync());
    }

    [Fact]
    public async Task RemoveOrphans_DeletesStateForMissingEdition_AndKeepsStateForExistingEdition()
    {
        var (_, keptEditionId) = await SeedAsync();
        var (_, goneEditionId) = await SeedAsync(isbn: "9780553293358");
        await AddStateAsync(CoverMirrorTarget.Edition, keptEditionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);
        await AddStateAsync(CoverMirrorTarget.Edition, goneEditionId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);

        await using (var db = _factory.CreateDbContext())
        {
            await db.Editions.Where(e => e.Id == goneEditionId).ExecuteDeleteAsync();
            await CoverMirrorQueue.RemoveOrphansAsync(db, CancellationToken.None);
        }

        await using var check = _factory.CreateDbContext();
        var remaining = await check.CoverMirrorStates.Select(s => s.TargetId).ToListAsync();
        Assert.Equal([keptEditionId], remaining);
    }

    [Fact]
    public async Task RemoveOrphans_DeletesBookStateForSoftDeletedBook()
    {
        var (liveBookId, _) = await SeedAsync(bookCoverUrl: CoverUrl);
        var (deletedBookId, _) = await SeedAsync(bookCoverUrl: CoverUrl, isbn: "9780553293358");
        await AddStateAsync(CoverMirrorTarget.Book, liveBookId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);
        await AddStateAsync(CoverMirrorTarget.Book, deletedBookId, CoverUrl, CoverMirrorOutcome.TransientFailure, Now);

        await using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.FirstAsync(b => b.Id == deletedBookId);
            book.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await CoverMirrorQueue.RemoveOrphansAsync(db, CancellationToken.None);
        }

        await using var check = _factory.CreateDbContext();
        var remaining = await check.CoverMirrorStates.Select(s => s.TargetId).ToListAsync();
        Assert.Equal([liveBookId], remaining);
    }

    private async Task<(int BookId, int EditionId)> SeedAsync(
        string editionCoverUrl = CoverUrl,
        string? bookCoverUrl = null,
        string isbn = "9780553293357")
    {
        await using var db = _factory.CreateDbContext();
        var edition = TestEditions.Edition(isbn: isbn, coverUrl: editionCoverUrl);
        var book = TestBooks.Book(
            "Foundation",
            coverUrl: bookCoverUrl,
            editions: [edition]);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return (book.Id, edition.Id);
    }

    private async Task AddStateAsync(
        CoverMirrorTarget target, int targetId, string url, CoverMirrorOutcome outcome, DateTime nextAttemptUtc)
    {
        await using var db = _factory.CreateDbContext();
        db.CoverMirrorStates.Add(new CoverMirrorState
        {
            Target = target,
            TargetId = targetId,
            Url = url,
            Attempts = 1,
            LastAttemptUtc = nextAttemptUtc.AddMinutes(-1),
            NextAttemptUtc = nextAttemptUtc,
            LastOutcome = outcome,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<int>> DueEditionIdsAsync(DateTime nowUtc)
    {
        await using var db = _factory.CreateDbContext();
        var due = await CoverMirrorQueue.DueEditionsAsync(db, nowUtc, CancellationToken.None);
        return due.Select(c => c.Id).ToList();
    }

    private async Task<CoverMirrorState> SingleStateAsync(CoverMirrorTarget target, int targetId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.CoverMirrorStates.SingleAsync(s => s.Target == target && s.TargetId == targetId);
    }
}
