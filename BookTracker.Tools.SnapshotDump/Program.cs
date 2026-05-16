using System.Text.Json;
using System.Text.Json.Serialization;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Tools.SnapshotDump;
using Microsoft.EntityFrameworkCore;

// ---- Parse args -------------------------------------------------------------
//
// Two flags supported:
//   --out <path>          Output file path. Default:
//                         ./snapshots/booktracker-{yyyy-MM-dd-HHmm}.json
//   --source <label>      Free-text label embedded in the snapshot's
//                         `source` field — typically "prod", "local",
//                         or "staging". Default: "unknown".
//
// Connection string comes from env var ConnectionStrings__DefaultConnection
// (matches Bookcase's config convention). The driver script
// (infra/dump-prod-to-json.ps1) sets this from Az-CLI-resolved prod
// SQL details before invoking; running locally against the Docker SQL
// container, set it manually first.

string? outPath = null;
string source = "unknown";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--source" when i + 1 < args.Length:
            source = args[++i];
            break;
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run --project BookTracker.Tools.SnapshotDump -- [--out <path>] [--source <label>]");
            Console.WriteLine();
            Console.WriteLine("Reads ConnectionStrings__DefaultConnection from env.");
            return 0;
    }
}

if (string.IsNullOrWhiteSpace(outPath))
{
    var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmm");
    outPath = Path.Combine("snapshots", $"booktracker-{stamp}.json");
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__DefaultConnection env var is not set.");
    Console.Error.WriteLine("Set it before running, or use infra/dump-prod-to-json.ps1 which sets it for you.");
    return 1;
}

// ---- Open DbContext + read all entities ------------------------------------
//
// Soft-deleted Books are excluded automatically by the global EF query
// filter on Book.DeletedAt — no IgnoreQueryFilters call here, by design.
//
// Each .Include / .ThenInclude chain is shaped so EF emits one bounded
// query per entity (with split-query for the deeper chains so the
// SQL Server cartesian explosion stays manageable). Catalogue size is
// O(thousands) so loading everything into memory is fine; the dump
// runs once per session, not in a hot loop.

var options = new DbContextOptionsBuilder<BookTrackerDbContext>()
    .UseSqlServer(connectionString, sql => sql.CommandTimeout(120))
    .Options;

Console.WriteLine($"Opening DbContext against the configured DefaultConnection...");
await using var db = new BookTrackerDbContext(options);

var bookEntities = await db.Books
    .AsNoTracking()
    .AsSplitQuery()
    .Include(b => b.Editions).ThenInclude(e => e.Copies)
    .Include(b => b.Editions).ThenInclude(e => e.Publisher)
    .Include(b => b.Works)
    .Include(b => b.Tags)
    .OrderBy(b => b.Id)
    .ToListAsync();

var workEntities = await db.Works
    .AsNoTracking()
    .AsSplitQuery()
    .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
    .Include(w => w.Genres)
    .Include(w => w.Books)
    .OrderBy(w => w.Id)
    .ToListAsync();

var authorEntities = await db.Authors
    .AsNoTracking()
    .Include(a => a.Aliases)
    .OrderBy(a => a.Id)
    .ToListAsync();

var genreEntities = await db.Genres
    .AsNoTracking()
    .Include(g => g.ParentGenre)
    .OrderBy(g => g.Id)
    .ToListAsync();

var seriesEntities = await db.Series.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
var publisherEntities = await db.Publishers.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
var tagEntities = await db.Tags.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
var wishlistEntities = await db.WishlistItems.AsNoTracking().OrderBy(w => w.Id).ToListAsync();

Console.WriteLine($"  Books: {bookEntities.Count}  Works: {workEntities.Count}  Authors: {authorEntities.Count}");
Console.WriteLine($"  Genres: {genreEntities.Count}  Series: {seriesEntities.Count}  Publishers: {publisherEntities.Count}");
Console.WriteLine($"  Tags: {tagEntities.Count}  Wishlist: {wishlistEntities.Count}");

// ---- Project to DTOs --------------------------------------------------------

var authorLookup = authorEntities.ToDictionary(a => a.Id);

var books = bookEntities.Select(b => new BookAnalysis(
    b.Id,
    b.Title,
    b.Category,
    b.Status,
    b.Rating,
    b.Notes,
    b.DateAdded,
    b.UpdatedAt,
    b.DefaultCoverArtUrl,
    b.Editions
        .OrderBy(e => e.Id)
        .Select(e => new EditionAnalysis(
            e.Id,
            e.Isbn,
            e.Format,
            e.DatePrinted,
            e.DatePrintedPrecision,
            e.CoverUrl,
            e.IsUserSupplied,
            e.Publisher?.Name,
            e.Copies
                .OrderBy(c => c.Id)
                .Select(c => new CopyAnalysis(c.Id, c.Condition, c.DateAcquired, c.Notes))
                .ToList()))
        .ToList(),
    b.Works.Select(w => w.Id).OrderBy(id => id).ToList(),
    b.Tags.Select(t => t.Name).OrderBy(n => n).ToList())).ToList();

var works = workEntities.Select(w => new WorkAnalysis(
    w.Id,
    w.Title,
    w.Subtitle,
    w.FirstPublishedDate,
    w.FirstPublishedDatePrecision,
    w.WorkAuthors
        .OrderBy(wa => wa.Order)
        .Select(wa => new WorkAuthorRef(wa.AuthorId, wa.Author.Name, wa.Order))
        .ToList(),
    w.Genres.Select(g => g.Name).OrderBy(n => n).ToList(),
    w.SeriesId,
    w.SeriesOrder,
    w.Books.Select(b => b.Id).OrderBy(id => id).ToList())).ToList();

var authors = authorEntities.Select(a => new AuthorAnalysis(
    a.Id,
    a.Name,
    a.CanonicalAuthorId,
    a.CanonicalAuthorId.HasValue && authorLookup.TryGetValue(a.CanonicalAuthorId.Value, out var canon) ? canon.Name : null,
    a.Aliases.Select(x => x.Id).OrderBy(id => id).ToList())).ToList();

var genres = genreEntities.Select(g => new GenreAnalysis(
    g.Id,
    g.Name,
    g.ParentGenreId,
    g.ParentGenre?.Name)).ToList();

var series = seriesEntities.Select(s => new SeriesAnalysis(
    s.Id, s.Name, s.Author, s.Type, s.ExpectedCount, s.Description)).ToList();

var publishers = publisherEntities.Select(p => new PublisherAnalysis(p.Id, p.Name)).ToList();
var tags = tagEntities.Select(t => new TagAnalysis(t.Id, t.Name)).ToList();

var wishlist = wishlistEntities.Select(w => new WishlistItemAnalysis(
    w.Id, w.Title, w.Author, w.Priority, w.Price, w.DateAdded, w.Isbn, w.SeriesId, w.SeriesOrder)).ToList();

var snapshot = new CatalogAnalysisSnapshot(
    GeneratedAtUtc: DateTime.UtcNow,
    Source: source,
    BookCount: books.Count,
    WorkCount: works.Count,
    AuthorCount: authors.Count,
    Books: books,
    Works: works,
    Authors: authors,
    Genres: genres,
    Series: series,
    Publishers: publishers,
    Tags: tags,
    WishlistItems: wishlist);

// ---- Serialise --------------------------------------------------------------

var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
{
    Directory.CreateDirectory(outDir);
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() }
};

await using (var stream = File.Create(outPath))
{
    await JsonSerializer.SerializeAsync(stream, snapshot, jsonOptions);
}

var info = new FileInfo(outPath);
Console.WriteLine();
Console.WriteLine($"Wrote {info.FullName}");
Console.WriteLine($"Size: {info.Length / 1024.0:N1} KB");
return 0;
