using System.ComponentModel.DataAnnotations;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Series membership lives on Works after the cutover. The page lists each
// Work in the series (in order) along with the Book(s) that contain it,
// so a short story republished in three compendiums shows once with three
// book references.
public class SeriesEditViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public SeriesFormInput? Input { get; private set; }
    public List<SeriesWorkRow> Works { get; private set; } = [];
    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }
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
                w.Books.Select(b => new ContainingBook(b.Id, b.Title)).ToList()))
            .ToList();
    }

    public async Task<int?> SaveAsync(int? seriesId)
    {
        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (IsNew)
            {
                var series = new Series
                {
                    Name = Input!.Name!.Trim(),
                    Author = string.IsNullOrWhiteSpace(Input.Author) ? null : Input.Author.Trim(),
                    Type = Input.Type,
                    ExpectedCount = Input.Type == SeriesType.Series ? Input.ExpectedCount : null,
                    Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim()
                };

                db.Series.Add(series);
                await db.SaveChangesAsync();
                return series.Id;
            }
            else
            {
                var series = await db.Series.FindAsync(seriesId!.Value);
                if (series is null) { NotFound = true; return null; }

                series.Name = Input!.Name!.Trim();
                series.Author = string.IsNullOrWhiteSpace(Input.Author) ? null : Input.Author.Trim();
                series.Type = Input.Type;
                series.ExpectedCount = Input.Type == SeriesType.Series ? Input.ExpectedCount : null;
                series.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();

                await db.SaveChangesAsync();
                SuccessMessage = "Series saved successfully.";
                return seriesId;
            }
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
            await using var db = await dbFactory.CreateDbContextAsync();
            var series = await db.Series.FindAsync(seriesId);
            if (series is not null)
            {
                db.Series.Remove(series);
                await db.SaveChangesAsync();
            }
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
                w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name).FirstOrDefault() ?? "",
                w.SeriesId))
            .ToListAsync();
    }

    public async Task AddWorkToSeriesAsync(int seriesId, int workId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works
            .Include(w => w.Books)
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) return;

        var nextOrder = Works.Count > 0 ? Works.Max(w => w.SeriesOrder ?? 0) + 1 : 1;

        work.SeriesId = seriesId;
        work.SeriesOrder = nextOrder;
        await db.SaveChangesAsync();

        Works.Add(new SeriesWorkRow(
            work.Id,
            work.Title,
            WorkAuthorshipFormatter.Display(work),
            nextOrder,
            work.Books.Select(b => new ContainingBook(b.Id, b.Title)).ToList()));
        WorkSearchResults.RemoveAll(r => r.Id == workId);
    }

    public async Task RemoveWorkFromSeriesAsync(int workId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works.FindAsync(workId);
        if (work is not null)
        {
            work.SeriesId = null;
            work.SeriesOrder = null;
            await db.SaveChangesAsync();
        }
        Works.RemoveAll(w => w.Id == workId);
    }

    public async Task UpdateWorkOrderAsync(int workId, int? newOrder)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works.FindAsync(workId);
        if (work is not null)
        {
            work.SeriesOrder = newOrder;
            await db.SaveChangesAsync();
        }

        var row = Works.FirstOrDefault(w => w.Id == workId);
        if (row is not null)
        {
            var idx = Works.IndexOf(row);
            Works[idx] = row with { SeriesOrder = newOrder };
        }
    }

    public record SeriesWorkRow(int Id, string Title, string Author, int? SeriesOrder, List<ContainingBook> Books);
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
