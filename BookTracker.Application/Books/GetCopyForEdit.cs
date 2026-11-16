using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model that prefills the Edit Copy dialog: the copy's fields plus the
// edition it belongs to. Null when the copy is gone. Relocated from
// CopyFormDialogViewModel's tracked FindAsync (#264).
public sealed record GetCopyForEdit(int CopyId) : IQuery<CopyForEdit?>;

public record CopyForEdit(
    int Id,
    int EditionId,
    BookCondition Condition,
    DateTime? DateAcquired,
    string? Notes);

public sealed class GetCopyForEditHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetCopyForEdit, CopyForEdit?>
{
    public async Task<CopyForEdit?> HandleAsync(GetCopyForEdit query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Copies
            .AsNoTracking()
            .Where(c => c.Id == query.CopyId)
            .Select(c => new CopyForEdit(c.Id, c.EditionId, c.Condition, c.DateAcquired, c.Notes))
            .FirstOrDefaultAsync(ct);
    }
}
