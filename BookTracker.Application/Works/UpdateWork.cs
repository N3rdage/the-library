using BookTracker.Application.Authors;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Updates an existing Work's fields (the Work-edit dialog): title,
/// subtitle, authorship, first-published date, series membership + order, and
/// genres. Dates and the series-order label arrive already parsed (the VM owns
/// the free-text parsing). Series membership targets an existing Series by id —
/// Series entity ops live in the Series feature.</summary>
public sealed record UpdateWork(
    int WorkId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<ContributorInput> Contributors,
    DateOnly? FirstPublished,
    DatePrecision Precision,
    IReadOnlyList<int> GenreIds,
    int? SeriesId,
    int? SeriesOrder,
    string? SeriesOrderDisplay) : ICommand;

public sealed class UpdateWorkHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<UpdateWork>
{
    public async Task HandleAsync(UpdateWork command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var work = await db.Works
            .Include(w => w.WorkAuthors)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == command.WorkId, ct)
            ?? throw new NotFoundException($"Work {command.WorkId} not found.");

        var authors = await AuthorResolver.FindOrCreateAllAsync(command.AuthorNames, db, ct);
        var contributors = new List<(Author Person, AuthorRole Role)>();
        foreach (var c in command.Contributors)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            contributors.Add((await AuthorResolver.FindOrCreateAsync(c.Name, db, ct), c.Role));
        }

        work.UpdateDetails(command.Title, command.Subtitle);
        work.AssignAuthorship(authors, contributors);
        work.SetFirstPublished(command.FirstPublished, command.Precision);
        if (command.SeriesId is int seriesId)
            work.AssignToSeries(seriesId, command.SeriesOrder, command.SeriesOrderDisplay);
        else
            work.ClearSeries();
        work.SetGenres(await CreateWorkOnBookHandler.ResolveGenresAsync(db, command.GenreIds, ct));

        await db.SaveChangesAsync(ct);
    }
}
