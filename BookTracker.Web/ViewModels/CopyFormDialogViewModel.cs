using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// Dialog VM for Add Copy (against an existing Edition) and Edit Copy,
// picked by IsNew flag.
public class CopyFormDialogViewModel(IDispatcher dispatcher) : DispatchingViewModel
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

        var copy = await dispatcher.Query(new GetCopyForEdit(copyId));
        if (copy is null) { NotFound = true; return; }

        EditionId = copy.EditionId;
        Condition = copy.Condition;
        DateAcquired = copy.DateAcquired;
        Notes = copy.Notes;
    }

    /// <returns>The copy id; null when the dialog state is stale or the domain
    /// refused (see <see cref="DispatchingViewModel.Error"/>).</returns>
    public async Task<int?> SaveAsync()
    {
        if (NotFound) return null;
        if (IsNew)
        {
            if (EditionId is not int eid) return null;
            return await DispatchAsync<int?>(async () =>
                await dispatcher.Send(new AddCopyToEdition(eid, Condition, DateAcquired, Notes)));
        }

        if (CopyId is not int cid) return null;
        return await DispatchAsync(() => dispatcher.Send(new UpdateCopy(cid, Condition, DateAcquired, Notes)))
            ? cid
            : null;
    }
}
