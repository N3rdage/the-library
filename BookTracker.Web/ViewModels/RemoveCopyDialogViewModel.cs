using System.Globalization;
using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// View model for RemoveCopyDialog. The page that opens the dialog creates one,
// seeds it with the copy whose "Remove" was pressed (Initialize), and passes it
// in as a parameter. The user picks why the copy left, the date and an optional
// note, and ConfirmAsync sends RemoveCopyFromCollection. A rule the handler
// refuses (the copy is on loan, already removed, or gone) comes back as Error
// and the dialog stays open.
public class RemoveCopyDialogViewModel(IDispatcher dispatcher, TimeProvider clock) : DispatchingViewModel
{
    /// <summary>The reasons a copy can leave the collection; Owned is not one.</summary>
    public static IReadOnlyList<CopyDisposition> Reasons { get; } =
        [CopyDisposition.Donated, CopyDisposition.Lost, CopyDisposition.Damaged, CopyDisposition.Sold];

    public static int NoteMaxLength => Copy.RemovalNoteMaxLength;

    public int CopyId { get; private set; }
    public string CopyLabel { get; private set; } = "";

    public CopyDisposition? Reason { get; set; }

    // DateTime? because that is what MudDatePicker binds; only the date part is used.
    public DateTime? RemovedOn { get; set; }
    public string? Note { get; set; }
    public bool Saving { get; private set; }

    public bool NoteTooLong => (Note?.Trim().Length ?? 0) > NoteMaxLength;

    public bool CanConfirm =>
        !Saving && CopyId > 0 && Reason is { } reason && Reasons.Contains(reason)
        && RemovedOn is not null && !NoteTooLong;

    public void Initialize(int copyId, string copyLabel)
    {
        CopyId = copyId;
        CopyLabel = copyLabel;
        Reason = null;
        RemovedOn = LoanDates.Today(clock).ToDateTime(TimeOnly.MinValue);
        Note = null;
        Error = null;
        Saving = false;
    }

    /// <summary>Removes the copy. True when it was saved; false (with
    /// <see cref="DispatchingViewModel.Error"/> set when the handler refused)
    /// otherwise.</summary>
    public async Task<bool> ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm) return false;

        Saving = true;
        try
        {
            return await DispatchAsync(() => dispatcher.Send(new RemoveCopyFromCollection(
                CopyId, Reason!.Value, DateOnly.FromDateTime(RemovedOn!.Value), Note), ct));
        }
        finally
        {
            Saving = false;
        }
    }

    /// <summary>"Donated · 3 Nov 2026", or just the reason when no date is
    /// recorded.</summary>
    public static string RemovalLabel(CopyDisposition reason, DateOnly? removedOn) =>
        removedOn is { } on
            ? $"{reason} · {on.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
            : reason.ToString();
}
