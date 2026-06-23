using System.ComponentModel.DataAnnotations;
using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Series membership lives on Works after the cutover. The page lists each
// Work in the series (in order) along with the Book(s) that contain it,
// so a short story republished in three compendiums shows once with three
// book references.
//
// Writes go through the Application layer (PR3b): create/edit/delete and the
// work-membership ops dispatch commands; reads (Initialize, SearchWorks) stay
// on the DbContext factory. The in-memory Works list is kept in sync after each
// mutation so the page doesn't reload.
public class SeriesEditViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IDispatcher dispatcher)
{
    public SeriesFormInput? Input { get; private set; }
    public List<SeriesWorkRow> Works { get; private set; } = [];
    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsNew { get; private set; }

    public bool ConfirmingDeleteSeries { get; set; }
    public bool Deleting { get; private set; }

    // Add work
    public bool ShowingAddWork { get; set; }
    public string WorkSearchTerm { get; set; } = "";
    public List<WorkSearchResult> WorkSearchResults { get; private set; } = [];

    public void InitializeNew()
    {
        IsNew = true;
        Input = new SeriesFormInput();
    }

    public async Task InitializeAsync(int seriesId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var series = await db.Series
            .Include(s => s.Works).ThenInclude(w => w.Books)
            .Include(s => s.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series is null)
        {
            NotFound = true;
            return;
        }

        Input = new SeriesFormInput
        {
            Name = series.Name,
            Author = series.Author,
            Type = series.Type,
            ExpectedCount = series.ExpectedCount,
            Description = series.Description
        };

        Works = series.Works
            .OrderBy(w => w.SeriesOrder ?? int.MaxValue)
            .ThenBy(w => w.Title)
            .Select(w => new SeriesWorkRow(
                w.Id,
                w.Title,
                WorkAuthorshipFormatter.Display(w),
                w.SeriesOrder,
                w.SeriesOrderDisplay,
                w.Books.Select(b => new ContainingBook(b.Id, b.Title)).ToList()))
            .ToList();
    }

    public async Task<int?> SaveAsync(int? seriesId)
    {
        Saving = true;
        // Clear both banners so a prior success + a new failure (or vice-versa)
        // can't render at once.
        ErrorMessage = null;
        SuccessMessage = null;
        // The aggregate normalises (trims name, nulls blank author/description,
        // drops ExpectedCount for a Collection), so pass the raw form values.
        try
        {
            if (IsNew)
            {
                return await dispatcher.Send(new CreateSeries(
                    Input!.Name!, Input.Author, Input.Type, Input.ExpectedCount, Input.Description));
            }
            else
            {
                await dispatcher.Send(new UpdateSeries(
                    seriesId!.Value, Input!.Name!, Input.Author, Input.Type, Input.ExpectedCount, Input.Description));
                SuccessMessage = "Series saved successfully.";
                return seriesId;
            }
        }
        catch (DomainRuleException ex)
        {
            // Duplicate (or blank) name — surface the friendly message and stay put.
            ErrorMessage = ex.Message;
            return null;
        }
        catch (NotFoundException)
        {
            // Series deleted out from under the editor between load and save.
            NotFound = true;
            return null;
        }
        finally
        {
            Saving = false;
        }
    }

    public async Task<bool> DeleteSeriesAsync(int seriesId)
    {
        Deleting = true;
        try
        {
            await dispatcher.Send(new DeleteSeries(seriesId)); // idempotent — no-op if already gone
            return true;
        }
        finally
        {
            Deleting = false;
        }
    }

    public async Task SearchWorksAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkSearchTerm))
        {
            WorkSearchResults = [];
            return;
        }

        var term = WorkSearchTerm.Trim();
        var currentWorkIds = Works.Select(w => w.Id).ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync();
        WorkSearchResults = await db.Works
            .Where(w => !currentWorkIds.Contains(w.Id))
            .Where(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term)))
            .OrderBy(w => w.Title)
            .Take(10)
            .Select(w => new WorkSearchResult(
                w.Id,
                w.Title,
                w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.Author.Name).FirstOrDefault() ?? "",
                w.SeriesId))
            .ToListAsync();
    }

    public async Task AddWorkToSeriesAsync(int seriesId, int workId)
    {
        if (Works.Any(w => w.Id == workId)) return; // already shown (e.g. a double-click) — no dup row

        await dispatcher.Send(new AddWorkToSeries(seriesId, workId)); // handler assigns the next order

        // Reload the work to build its row (and read the order the handler set).
        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works
            .Include(w => w.Books)
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) return;

        Works.Add(new SeriesWorkRow(
            work.Id,
            work.Title,
            WorkAuthorshipFormatter.Display(work),
            work.SeriesOrder,
            work.SeriesOrderDisplay,
            work.Books.Select(b => new ContainingBook(b.Id, b.Title)).ToList()));
        WorkSearchResults.RemoveAll(r => r.Id == workId);
    }

    public async Task RemoveWorkFromSeriesAsync(int workId)
    {
        await dispatcher.Send(new RemoveWorkFromSeries(workId));
        Works.RemoveAll(w => w.Id == workId);
    }

    public async Task UpdateWorkOrderAsync(int workId, string? rawOrder)
    {
        // Free-text so "4.5" interquels survive: the VM owns parsing into the
        // integer sort key + optional display override; the handler just stores them.
        var (order, display) = SeriesOrderParser.Parse(rawOrder);
        await dispatcher.Send(new SetWorkSeriesOrder(workId, order, display));

        var row = Works.FirstOrDefault(w => w.Id == workId);
        if (row is not null)
        {
            var idx = Works.IndexOf(row);
            Works[idx] = row with { SeriesOrder = order, SeriesOrderDisplay = display };
        }
    }

    public record SeriesWorkRow(int Id, string Title, string Author, int? SeriesOrder, string? SeriesOrderDisplay, List<ContainingBook> Books);
    public record ContainingBook(int Id, string Title);
    public record WorkSearchResult(int Id, string Title, string Author, int? CurrentSeriesId);

    public class SeriesFormInput
    {
        [Required, StringLength(300)]
        public string? Name { get; set; }

        [StringLength(200)]
        public string? Author { get; set; }

        public SeriesType Type { get; set; } = SeriesType.Series;

        public int? ExpectedCount { get; set; }

        public string? Description { get; set; }
    }
}
