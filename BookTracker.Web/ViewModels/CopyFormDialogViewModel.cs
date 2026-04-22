using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog VM for Add Copy (against an existing Edition) and Edit Copy,
// picked by IsNew flag.
public class CopyFormDialogViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool IsNew { get; private set; }
    public bool NotFound { get; private set; }
    public int? EditionId { get; private set; }
    public int? CopyId { get; private set; }

    public BookCondition Condition { get; set; } = BookCondition.Good;
    public DateTime? DateAcquired { get; set; }
    public string? Notes { get; set; }

    public void InitializeForAdd(int editionId)
    {
        IsNew = true;
        EditionId = editionId;
        CopyId = null;
        DateAcquired = DateTime.UtcNow.Date;
    }

    public async Task InitializeForEditAsync(int copyId)
    {
        IsNew = false;
        CopyId = copyId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = await db.Copies.FindAsync(copyId);
        if (copy is null) { NotFound = true; return; }

        EditionId = copy.EditionId;
        Condition = copy.Condition;
        DateAcquired = copy.DateAcquired;
        Notes = copy.Notes;
    }

    public async Task<int?> SaveAsync()
    {
        if (NotFound) return null;

        await using var db = await dbFactory.CreateDbContextAsync();

        if (IsNew)
        {
            if (EditionId is not int eid) return null;
            var copy = new Copy
            {
                EditionId = eid,
                Condition = Condition,
                DateAcquired = DateAcquired,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            };
            db.Copies.Add(copy);
            await db.SaveChangesAsync();
            return copy.Id;
        }
        else
        {
            if (CopyId is not int cid) return null;
            var copy = await db.Copies.FindAsync(cid);
            if (copy is null) return null;

            copy.Condition = Condition;
            copy.DateAcquired = DateAcquired;
            copy.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

            await db.SaveChangesAsync();
            return copy.Id;
        }
    }
}
