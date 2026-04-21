using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class DuplicateDetectionServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private DuplicateDetectionService CreateService() => new(_factory);

    // ─── Authors ──────────────────────────────────────────────────────

    [Fact]
    public async Task Authors_detects_two_with_normalised_match()
    {
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        var report = await CreateService().DetectAllAsync();

        var pair = Assert.Single(report.Authors);
        Assert.Contains("J.R.R. Tolkien", new[] { pair.Lower.Name, pair.Higher.Name });
        Assert.Contains("JRR Tolkien", new[] { pair.Lower.Name, pair.Higher.Name });
        Assert.Null(pair.Dismissed);
    }

    [Fact]
    public async Task Authors_detects_initials_with_spaces()
    {
        await SeedAuthorsAsync("J R R Tolkien", "JRR Tolkien");

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Authors);
    }

    [Fact]
    public async Task Authors_ignores_non_matching_names()
    {
        await SeedAuthorsAsync("Stephen King", "Richard Bachman");

        var report = await CreateService().DetectAllAsync();

        Assert.Empty(report.Authors);
    }

    [Fact]
    public async Task Authors_emits_three_pairs_for_three_way_match()
    {
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien", "J R R Tolkien");

        var report = await CreateService().DetectAllAsync();

        Assert.Equal(3, report.Authors.Count);
    }

    [Fact]
    public async Task Authors_detects_surname_plus_first_initial_variants()
    {
        // The Preston case that prompted loosening the matcher: full first,
        // diminutive, and initial-only should all be flagged together.
        await SeedAuthorsAsync("Douglas Preston", "Doug Preston", "D Preston");

        var report = await CreateService().DetectAllAsync();

        // Three authors in one matching group → 3 pairs.
        Assert.Equal(3, report.Authors.Count);
        Assert.All(report.Authors, p =>
            Assert.Contains("surname", p.MatchReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Authors_surname_matcher_is_case_insensitive_for_typos()
    {
        // Shift-key typos like "PReston" should still normalise to "preston".
        await SeedAuthorsAsync("Douglas Preston", "Douglas PReston");

        var report = await CreateService().DetectAllAsync();

        // Exact-after-normalise should catch this — tighter reason wins.
        var pair = Assert.Single(report.Authors);
        Assert.Equal("Names normalise to the same value", pair.MatchReason);
    }

    [Fact]
    public async Task Authors_surname_matcher_does_not_flag_different_surnames()
    {
        await SeedAuthorsAsync("Stephen King", "Stephen Hawking");

        var report = await CreateService().DetectAllAsync();

        Assert.Empty(report.Authors);
    }

    [Fact]
    public async Task Authors_surname_matcher_reports_only_once_when_both_strategies_match()
    {
        // Two exact-matching variants also trivially match surname+initial.
        // The tighter strategy should claim the pair; no duplicate row.
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        var report = await CreateService().DetectAllAsync();

        var pair = Assert.Single(report.Authors);
        Assert.Equal("Names normalise to the same value", pair.MatchReason);
    }

    // ─── Works ────────────────────────────────────────────────────────

    [Fact]
    public async Task Works_detects_same_author_same_normalised_title()
    {
        await SeedWorkAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedWorkAsync("Hobbit", "J.R.R. Tolkien");

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Works);
    }

    [Fact]
    public async Task Works_ignores_same_title_different_authors()
    {
        await SeedWorkAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedWorkAsync("The Hobbit", "Someone Else");

        var report = await CreateService().DetectAllAsync();

        Assert.Empty(report.Works);
    }

    // ─── Books ────────────────────────────────────────────────────────

    [Fact]
    public async Task Books_detects_same_author_same_normalised_title()
    {
        await SeedBookAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedBookAsync("Hobbit", "J.R.R. Tolkien");

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Books);
        Assert.Contains("Same author and normalised title", report.Books[0].MatchReason);
    }

    [Fact]
    public async Task Books_detects_same_author_same_work_set()
    {
        // Two Books that share the same Work — the "captured twice, should
        // have been one Book with two Editions" case.
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "J.R.R. Tolkien" };
        var work = new Work { Title = "The Hobbit", Author = author };
        db.Books.Add(new Book { Title = "Hobbit HB", Works = [work] });
        db.Books.Add(new Book { Title = "Hobbit PB", Works = [work] });
        await db.SaveChangesAsync();

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Books);
        Assert.Contains("same set of Works", report.Books[0].MatchReason);
    }

    [Fact]
    public async Task Books_does_not_emit_same_pair_twice_when_both_strategies_match()
    {
        // Same title AND same work-set: dedup so the pair appears once.
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "J.R.R. Tolkien" };
        var work = new Work { Title = "The Hobbit", Author = author };
        db.Books.Add(new Book { Title = "The Hobbit", Works = [work] });
        db.Books.Add(new Book { Title = "The Hobbit", Works = [work] });
        await db.SaveChangesAsync();

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Books);
    }

    // ─── Editions ─────────────────────────────────────────────────────

    [Fact]
    public async Task Editions_detects_no_isbn_same_format_publisher_date()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var work = new Work { Title = "Murder on the Orient Express", Author = author };
        var publisher = new Publisher { Name = "Collins Crime Club" };
        var book = new Book
        {
            Title = "Murder on the Orient Express",
            Works = [work],
            Editions =
            [
                new Edition { Isbn = null, Format = BookFormat.Hardcover, Publisher = publisher, DatePrinted = new DateOnly(1934, 1, 1) },
                new Edition { Isbn = null, Format = BookFormat.Hardcover, Publisher = publisher, DatePrinted = new DateOnly(1934, 1, 1) }
            ]
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var report = await CreateService().DetectAllAsync();

        Assert.Single(report.Editions);
    }

    [Fact]
    public async Task Editions_ignores_differing_format_on_no_isbn_match()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var work = new Work { Title = "Murder on the Orient Express", Author = author };
        var publisher = new Publisher { Name = "Collins Crime Club" };
        var book = new Book
        {
            Title = "Murder on the Orient Express",
            Works = [work],
            Editions =
            [
                new Edition { Isbn = null, Format = BookFormat.Hardcover, Publisher = publisher, DatePrinted = new DateOnly(1934, 1, 1) },
                new Edition { Isbn = null, Format = BookFormat.TradePaperback, Publisher = publisher, DatePrinted = new DateOnly(1934, 1, 1) }
            ]
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var report = await CreateService().DetectAllAsync();

        Assert.Empty(report.Editions);
    }

    // ─── Dismiss / Unignore ───────────────────────────────────────────

    [Fact]
    public async Task Dismiss_moves_pair_to_dismissed_list_and_carries_ignored_id()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");
        var svc = CreateService();

        await svc.DismissAsync(DuplicateEntityType.Author, a, b, "intentional separate entries");

        var report = await svc.DetectAllAsync();
        Assert.Single(report.Authors);
        var pair = report.Authors[0];
        Assert.NotNull(pair.Dismissed);
        Assert.Equal("intentional separate entries", pair.Dismissed!.Note);
    }

    [Fact]
    public async Task Dismiss_is_idempotent()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");
        var svc = CreateService();

        await svc.DismissAsync(DuplicateEntityType.Author, a, b, null);
        await svc.DismissAsync(DuplicateEntityType.Author, a, b, "second call");

        using var db = _factory.CreateDbContext();
        Assert.Single(db.IgnoredDuplicates);
    }

    [Fact]
    public async Task Dismiss_normalises_id_order()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");
        var svc = CreateService();

        // Pass IDs in reverse — should still dedupe with the lower/higher row.
        await svc.DismissAsync(DuplicateEntityType.Author, b, a, null);
        await svc.DismissAsync(DuplicateEntityType.Author, a, b, null);

        using var db = _factory.CreateDbContext();
        Assert.Single(db.IgnoredDuplicates);
    }

    [Fact]
    public async Task Unignore_removes_row_and_pair_returns_to_active()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");
        var svc = CreateService();

        await svc.DismissAsync(DuplicateEntityType.Author, a, b, null);
        var afterDismiss = await svc.DetectAllAsync();
        var ignoredId = afterDismiss.Authors[0].Dismissed!.IgnoredDuplicateId;

        await svc.UnignoreAsync(ignoredId);

        var afterUnignore = await svc.DetectAllAsync();
        Assert.Null(afterUnignore.Authors[0].Dismissed);
    }

    // ─── Orphan cleanup ───────────────────────────────────────────────

    [Fact]
    public async Task Orphaned_ignored_rows_are_swept_on_detect()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Author,
                LowerId = 999,
                HigherId = 1000,
                DismissedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await CreateService().DetectAllAsync();

        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.IgnoredDuplicates);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int, int)> SeedAuthorsAsync(params string[] names)
    {
        using var db = _factory.CreateDbContext();
        var authors = names.Select(n => new Author { Name = n }).ToList();
        db.Authors.AddRange(authors);
        await db.SaveChangesAsync();
        return (authors[0].Id, authors.Count > 1 ? authors[1].Id : 0);
    }

    private async Task SeedWorkAsync(string title, string authorName)
    {
        using var db = _factory.CreateDbContext();
        // Use existing author if same name to respect the real schema
        // (Author.Name is unique).
        var existing = db.Authors.FirstOrDefault(a => a.Name == authorName);
        var author = existing ?? new Author { Name = authorName };
        var book = new Book
        {
            Title = title,
            Works = [new Work { Title = title, Author = author }]
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
    }

    private async Task SeedBookAsync(string title, string authorName)
    {
        await SeedWorkAsync(title, authorName);
    }
}
