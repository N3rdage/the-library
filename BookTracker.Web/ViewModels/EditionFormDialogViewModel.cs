using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog VM for Add Edition and Edit Edition, picked by IsNew flag.
//
// Add:
//   InitializeForAddAsync(bookId) — blank form, lookup button available,
//   FirstCopyCondition captured so save creates Edition + first Copy.
// Edit:
//   InitializeForEditAsync(editionId) — prefills existing fields, no
//   lookup, no first-copy field. Save updates in place.
//
// Publisher is typeahead find-or-create (same shape as Author on the
// Work dialog). ISBN is optional (nullable, filtered unique) so
// pre-1974 editions save without one.
public class EditionFormDialogViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup)
{
    public bool IsNew { get; private set; }
    public bool NotFound { get; private set; }
    public int BookId { get; private set; }
    public int? EditionId { get; private set; }

    public string? Isbn { get; set; }
    public BookFormat Format { get; set; } = BookFormat.TradePaperback;
    public string? Publisher { get; set; }
    public string FirstPublishedOrPrintedDate { get; set; } = "";
    public string? CoverUrl { get; set; }

    // Add-only: the first Copy that ships with a new Edition. Every
    // Edition needs at least one Copy, so the Add flow captures it here.
    public BookCondition FirstCopyCondition { get; set; } = BookCondition.Good;

    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }

    public async Task InitializeForAddAsync(int bookId)
    {
        IsNew = true;
        BookId = bookId;
        EditionId = null;
    }

    public async Task InitializeForEditAsync(int editionId)
    {
        IsNew = false;
        EditionId = editionId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var edition = await db.Editions.Include(e => e.Publisher).FirstOrDefaultAsync(e => e.Id == editionId);
        if (edition is null) { NotFound = true; return; }

        BookId = edition.BookId;
        Isbn = edition.Isbn;
        Format = edition.Format;
        Publisher = edition.Publisher?.Name;
        FirstPublishedOrPrintedDate = PartialDateParser.Format(edition.DatePrinted, edition.DatePrintedPrecision);
        CoverUrl = edition.CoverUrl;
    }

    public async Task<IEnumerable<string>> SearchPublishersAsync(string query, CancellationToken ct)
    {
        // .ToLower() inside the Where keeps behaviour consistent across
        // providers. SQL Server defaults to case-insensitive collation so
        // raw Contains would match either way in prod, but the EF InMemory
        // provider used in tests is case-sensitive — explicit lowering
        // prevents tests and prod from disagreeing.
        var q = (query ?? "").Trim().ToLower();
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = db.Publishers.AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            matches = matches.Where(p => p.Name.ToLower().Contains(q));
        }
        return await matches
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task LookupAsync()
    {
        LookupMessage = null;
        if (string.IsNullOrWhiteSpace(Isbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            return;
        }

        LookingUp = true;
        try
        {
            var result = await lookup.LookupByIsbnAsync(Isbn, CancellationToken.None);
            if (result is null)
            {
                LookupMessage = $"No match found for ISBN {Isbn}.";
                return;
            }

            // Auto-fill empties only — don't clobber anything the user typed.
            if (string.IsNullOrWhiteSpace(Isbn)) Isbn = result.Isbn;
            if (string.IsNullOrWhiteSpace(Publisher)) Publisher = result.Publisher;
            if (string.IsNullOrWhiteSpace(CoverUrl)) CoverUrl = result.CoverUrl;
            if (string.IsNullOrWhiteSpace(FirstPublishedOrPrintedDate) && result.DatePrinted is DateOnly d)
            {
                FirstPublishedOrPrintedDate = PartialDateParser.Format(d, result.DatePrintedPrecision);
            }
            if (result.Format is BookFormat fmt) Format = fmt;

            LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";
        }
        finally
        {
            LookingUp = false;
        }
    }

    /// <returns>The new or updated Edition id.</returns>
    public async Task<int?> SaveAsync()
    {
        if (NotFound) return null;

        await using var db = await dbFactory.CreateDbContextAsync();

        // Resolve Publisher via find-or-create.
        Publisher? publisher = null;
        var pubName = Publisher?.Trim();
        if (!string.IsNullOrEmpty(pubName))
        {
            publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
            if (publisher is null)
            {
                publisher = new Publisher { Name = pubName };
                db.Publishers.Add(publisher);
            }
        }

        var datePrinted = PartialDateParser.TryParse(FirstPublishedOrPrintedDate) ?? PartialDate.Empty;

        if (IsNew)
        {
            var edition = new Edition
            {
                BookId = BookId,
                Isbn = string.IsNullOrWhiteSpace(Isbn) ? null : Isbn.Trim(),
                Format = Format,
                DatePrinted = datePrinted.Date,
                DatePrintedPrecision = datePrinted.Precision,
                Publisher = publisher,
                CoverUrl = string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim(),
                Copies = [new Copy { Condition = FirstCopyCondition }],
            };
            db.Editions.Add(edition);
            await db.SaveChangesAsync();
            return edition.Id;
        }
        else
        {
            if (EditionId is not int id) return null;
            var edition = await db.Editions.FindAsync(id);
            if (edition is null) return null;

            edition.Isbn = string.IsNullOrWhiteSpace(Isbn) ? null : Isbn.Trim();
            edition.Format = Format;
            edition.DatePrinted = datePrinted.Date;
            edition.DatePrintedPrecision = datePrinted.Precision;
            edition.PublisherId = publisher?.Id;
            edition.Publisher = publisher;
            edition.CoverUrl = string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim();

            await db.SaveChangesAsync();
            return edition.Id;
        }
    }
}
