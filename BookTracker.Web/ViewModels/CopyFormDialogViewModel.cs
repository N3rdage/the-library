using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

/// <summary>What closing the copy dialog asks the page to do besides reload.</summary>
public enum CopyFormOutcome { Restored, RemoveRequested }

// Dialog VM for Add Copy (against an existing Edition) and Edit Copy,
// picked by IsNew flag. Editing a copy also offers Restore for a removed copy
// and, for an owned copy not on loan, a request to remove it (the page runs
// the remove dialog).
public class CopyFormDialogViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    private (BookCondition Condition, DateTime? DateAcquired, string? Notes) _loaded;
    private bool _isOwned = true;
    private bool _isLent;

    public bool IsNew { get; private set; }
    public bool NotFound { get; private set; }
    public int? EditionId { get; private set; }
    public int? CopyId { get; private set; }

    public BookCondition Condition { get; set; } = BookCondition.Good;
    public DateTime? DateAcquired { get; set; }
    public string? Notes { get; set; }

    /// <summary>"Donated · 3 Nov 2026" when the copy has left the collection;
    /// null while it is owned.</summary>
    public string? RemovalLabel { get; private set; }

    /// <summary>True when the form differs from the copy as loaded.</summary>
    public bool IsDirty =>
        !IsNew && (Condition, DateAcquired, string.IsNullOrEmpty(Notes) ? null : Notes)
            != (_loaded.Condition, _loaded.DateAcquired, _loaded.Notes);

    /// <summary>Remove is offered for an existing, owned copy that is not on loan.</summary>
    public bool CanRemove => !IsNew && !NotFound && _isOwned && !_isLent;

    /// <summary>Restore is offered for an existing copy that has been removed.</summary>
    public bool CanRestore => !IsNew && !NotFound && !_isOwned;

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
        _loaded = (Condition, DateAcquired, string.IsNullOrEmpty(Notes) ? null : Notes);
        _isOwned = copy.Disposition == CopyDisposition.Owned;
        _isLent = copy.IsLent;
        RemovalLabel = _isOwned
            ? null
            : RemoveCopyDialogViewModel.RemovalLabel(copy.Disposition, copy.RemovedOn);
    }

    /// <summary>Brings a removed copy back into the collection. False, with
    /// <see cref="DispatchingViewModel.Error"/> set, when it was refused.</summary>
    public async Task<bool> RestoreAsync()
    {
        if (!CanRestore || CopyId is not int cid) return false;
        return await DispatchAsync(() => dispatcher.Send(new RestoreCopyToCollection(cid)));
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
