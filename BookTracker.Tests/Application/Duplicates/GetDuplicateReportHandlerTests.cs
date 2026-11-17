using BookTracker.Application.Duplicates;
using BookTracker.Data.Models;

namespace BookTracker.Tests.Application.Duplicates;

[Trait("Category", TestCategories.Integration)]
public class GetDuplicateReportHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<DuplicateReport> DetectAsync() =>
        new GetDuplicateReportHandler(_factory).HandleAsync(new GetDuplicateReport());

    // ─── Authors ──────────────────────────────────────────────────────

    [Fact]
    public async Task Authors_detects_two_with_normalised_match()
    {
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        var report = await DetectAsync();

        var pair = Assert.Single(report.Authors);
        Assert.Contains("J.R.R. Tolkien", new[] { pair.Lower.Name, pair.Higher.Name });
        Assert.Contains("JRR Tolkien", new[] { pair.Lower.Name, pair.Higher.Name });
        Assert.Null(pair.Dismissed);
    }

    [Fact]
    public async Task Authors_detects_initials_with_spaces()
    {
        await SeedAuthorsAsync("J R R Tolkien", "JRR Tolkien");

        var report = await DetectAsync();

        Assert.Single(report.Authors);
    }

    [Fact]
    public async Task Authors_ignores_non_matching_names()
    {
        await SeedAuthorsAsync("Stephen King", "Richard Bachman");

        var report = await DetectAsync();

        Assert.Empty(report.Authors);
    }

    [Fact]
    public async Task Authors_emits_three_pairs_for_three_way_match()
    {
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien", "J R R Tolkien");

        var report = await DetectAsync();

        Assert.Equal(3, report.Authors.Count);
    }

    [Fact]
    public async Task Authors_detects_surname_plus_first_initial_variants()
    {
        // The Preston case that prompted loosening the matcher: full first,
        // diminutive, and initial-only should all be flagged together.
        await SeedAuthorsAsync("Douglas Preston", "Doug Preston", "D Preston");

        var report = await DetectAsync();

        // Three authors in one matching group → 3 pairs.
        Assert.Equal(3, report.Authors.Count);
        Assert.All(report.Authors, p =>
            Assert.Contains("surname", p.MatchReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Test seeds two Authors that differ only in case ('Douglas Preston' vs 'Douglas PReston'), which the unique Name index can'\''t store under SQL Server'\''s default case-insensitive collation. The detection logic is still useful in principle (defensive against legacy data with case typos), but the schema prevents this state from arising organically. Keep test for documentation; un-skip if Author.Name moves to a case-sensitive collation or the unique constraint is dropped.")]
    public async Task Authors_surname_matcher_is_case_insensitive_for_typos()
    {
        // Shift-key typos like "PReston" should still normalise to "preston".
        await SeedAuthorsAsync("Douglas Preston", "Douglas PReston");

        var report = await DetectAsync();

        // Exact-after-normalise should catch this — tighter reason wins.
        var pair = Assert.Single(report.Authors);
        Assert.Equal("Names normalise to the same value", pair.MatchReason);
    }

    [Fact]
    public async Task Authors_surname_matcher_does_not_flag_different_surnames()
    {
        await SeedAuthorsAsync("Stephen King", "Stephen Hawking");

        var report = await DetectAsync();

        Assert.Empty(report.Authors);
    }

    [Fact]
    public async Task Authors_surname_matcher_reports_only_once_when_both_strategies_match()
    {
        // Two exact-matching variants also trivially match surname+initial.
        // The tighter strategy should claim the pair; no duplicate row.
        await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        var report = await DetectAsync();

        var pair = Assert.Single(report.Authors);
        Assert.Equal("Names normalise to the same value", pair.MatchReason);
    }

    // ─── Works ────────────────────────────────────────────────────────

    [Fact]
    public async Task Works_detects_same_author_same_normalised_title()
    {
        await SeedWorkAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedWorkAsync("Hobbit", "J.R.R. Tolkien");

        var report = await DetectAsync();

        Assert.Single(report.Works);
    }

    [Fact]
    public async Task Works_ignores_same_title_different_authors()
    {
        await SeedWorkAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedWorkAsync("The Hobbit", "Someone Else");

        var report = await DetectAsync();

        Assert.Empty(report.Works);
    }

    // ─── Books ────────────────────────────────────────────────────────

    [Fact]
    public async Task Books_detects_same_author_same_normalised_title()
    {
        await SeedBookAsync("The Hobbit", "J.R.R. Tolkien");
        await SeedBookAsync("Hobbit", "J.R.R. Tolkien");

        var report = await DetectAsync();

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
        var work = new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        db.Books.Add(new Book { Title = "Hobbit HB", Works = [work] });
        db.Books.Add(new Book { Title = "Hobbit PB", Works = [work] });
        await db.SaveChangesAsync();

        var report = await DetectAsync();

        Assert.Single(report.Books);
        Assert.Contains("same set of Works", report.Books[0].MatchReason);
    }

    [Fact]
    public async Task Books_does_not_emit_same_pair_twice_when_both_strategies_match()
    {
        // Same title AND same work-set: dedup so the pair appears once.
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "J.R.R. Tolkien" };
        var work = new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        db.Books.Add(new Book { Title = "The Hobbit", Works = [work] });
        db.Books.Add(new Book { Title = "The Hobbit", Works = [work] });
        await db.SaveChangesAsync();

        var report = await DetectAsync();

        Assert.Single(report.Books);
    }

    // ─── Editions ─────────────────────────────────────────────────────

    [Fact]
    public async Task Editions_detects_no_isbn_same_format_publisher_date()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var work = new Work { Title = "Murder on the Orient Express", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
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

        var report = await DetectAsync();

        Assert.Single(report.Editions);
    }

    [Fact]
    public async Task Editions_ignores_differing_format_on_no_isbn_match()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var work = new Work { Title = "Murder on the Orient Express", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
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

        var report = await DetectAsync();

        Assert.Empty(report.Editions);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task SeedAuthorsAsync(params string[] names)
    {
        using var db = _factory.CreateDbContext();
        db.Authors.AddRange(names.Select(n => new Author { Name = n }));
        await db.SaveChangesAsync();
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
            Works = [new Work { Title = title, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
    }

    private async Task SeedBookAsync(string title, string authorName)
    {
        await SeedWorkAsync(title, authorName);
    }
}
