using BookTracker.Application.Catalog;
using BookTracker.Data.Models;
using BookTracker.Shared.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Integration tests for the catalog-snapshot read-model handler against the SQL
// container. Relocated from CatalogSnapshotServiceTests when the projection
// moved to BookTracker.Application.Catalog (PR6). Version is passed as "dev"
// (the host stamps the real SHA).
[Trait("Category", TestCategories.Integration)]
public class GetCatalogSnapshotHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<CatalogSnapshot> GetSnapshot(DateTime? since = null) =>
        new GetCatalogSnapshotHandler(_factory).HandleAsync(new GetCatalogSnapshot(since, "dev"));

    [Fact]
    public async Task GetSnapshotAsync_PrimaryAndAllAuthors_OnSingleWorkBook()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.Add(new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var book = Assert.Single(snapshot.Books);
        Assert.Equal("Foundation", book.Title);
        Assert.Equal("Isaac Asimov", book.PrimaryAuthor);
        Assert.Equal([new AuthorContribution("Isaac Asimov", "Author")], book.AllAuthors);
    }

    [Fact]
    public async Task GetSnapshotAsync_PrimaryAndAllAuthors_OnMultiWorkCompendium()
    {
        // Compendium with three Works, each by a different author. Primary
        // author = first Work's lowest-Order WorkAuthor; AllAuthors lists
        // every credited contributor.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            var bradbury = new Author { Name = "Ray Bradbury" };
            db.Authors.AddRange(asimov, king, bradbury);
            db.Books.Add(new Book
            {
                Title = "The Funhouse",
                Works =
                [
                    new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                    new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                    new Work { Title = "Bradbury story", WorkAuthors = [new WorkAuthor { Author = bradbury, Order = 0 }] },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var book = Assert.Single(snapshot.Books);
        Assert.Equal("Isaac Asimov", book.PrimaryAuthor);
        Assert.Equal([
            new AuthorContribution("Isaac Asimov", "Author"),
            new AuthorContribution("Stephen King", "Author"),
            new AuthorContribution("Ray Bradbury", "Author"),
        ], book.AllAuthors);
    }

    [Fact]
    public async Task GetSnapshotAsync_AliasBookCounts_RolledUpAtCanonical_NotAtAlias()
    {
        // King canonical, Bachman alias. Carrie credited to King, The Long
        // Walk credited to Bachman. Canonical row should show the rolled-up
        // count (2); alias row shows just its direct count (1).
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);
            db.Books.AddRange(
                new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] },
                new Book { Title = "The Long Walk", Works = [new Work { Title = "The Long Walk", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var kingRow = snapshot.Authors.Single(a => a.Name == "Stephen King");
        var bachmanRow = snapshot.Authors.Single(a => a.Name == "Richard Bachman");

        Assert.Equal(kingRow.Id, kingRow.CanonicalId);
        Assert.Equal(2, kingRow.BookCount);

        Assert.Equal(kingRow.Id, bachmanRow.CanonicalId);
        Assert.Equal(1, bachmanRow.BookCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_BookCreditedToBothCanonicalAndAlias_CountedPerCreditingMember()
    {
        // Edge case: a single book has WorkAuthors crediting both King AND
        // Bachman (rare but possible — e.g. a foreword/author note that
        // names the alias). The canonical rollup is a SUM of member counts, so
        // such a book is counted once per crediting member — a deliberate
        // accepted imprecision (see AuthorRollups), traded for dropping a
        // cross-member DISTINCT for numeric perfection nobody can see. The only
        // real instance in ~2000 books is King's own-name Bachman omnibus.
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);
            db.Books.Add(new Book
            {
                Title = "The Bachman Books",
                Works = [new Work
                {
                    Title = "The Bachman Books",
                    WorkAuthors =
                    [
                        new WorkAuthor { Author = king, Order = 0 },
                        new WorkAuthor { Author = bachman, Order = 1 },
                    ]
                }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var kingRow = snapshot.Authors.Single(a => a.Name == "Stephen King");

        // Credited to both King AND Bachman (whose canonical is King), so the
        // summed rollup counts it once per member = 2. Accepted (see above).
        Assert.Equal(2, kingRow.BookCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_CollectsIsbnsAcrossEditions()
    {
        // A Book with two Editions, different ISBNs — both should appear
        // in BookSnapshot.Isbns. Empty / null ISBNs are filtered out.
        using (var db = _factory.CreateDbContext())
        {
            var clarke = new Author { Name = "Arthur C. Clarke" };
            db.Authors.Add(clarke);
            db.Books.Add(new Book
            {
                Title = "Rendezvous with Rama",
                Works = [new Work { Title = "Rendezvous with Rama", WorkAuthors = [new WorkAuthor { Author = clarke, Order = 0 }] }],
                Editions =
                [
                    new Edition { Isbn = "9780553287899", Format = BookFormat.MassMarketPaperback },
                    new Edition { Isbn = "9780575094192", Format = BookFormat.TradePaperback },
                    new Edition { Isbn = null, Format = BookFormat.Hardcover },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var book = Assert.Single(snapshot.Books);

        Assert.Equal(2, book.Isbns.Count);
        Assert.Contains("9780553287899", book.Isbns);
        Assert.Contains("9780575094192", book.Isbns);
    }

    [Fact]
    public async Task GetSnapshotAsync_VersionAndSyncedAtPopulated()
    {
        // Catalog version is the deployed commit SHA (or "dev" locally)
        // so the SW can detect a deploy and invalidate cached snapshots.
        // SyncedAt is server clock at projection time.
        var before = DateTime.UtcNow.AddSeconds(-5);
        var snapshot = await GetSnapshot();
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.False(string.IsNullOrWhiteSpace(snapshot.Version));
        Assert.InRange(snapshot.SyncedAt, before, after);
    }

    [Fact]
    public async Task GetSnapshotAsync_BookStatusAndRatingProjected()
    {
        // Status + rating need to make the trip — the bookshop result
        // card displays both.
        using (var db = _factory.CreateDbContext())
        {
            var clarke = new Author { Name = "Arthur C. Clarke" };
            db.Authors.Add(clarke);
            db.Books.Add(new Book
            {
                Title = "2001: A Space Odyssey",
                Status = BookStatus.Read,
                Rating = 5,
                Works = [new Work { Title = "2001", WorkAuthors = [new WorkAuthor { Author = clarke, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var book = Assert.Single(snapshot.Books);

        // Status is serialised as the enum's string name, not its
        // underlying int — the BookTracker.Shared DTOs are wire
        // contracts and stay free of the BookTracker.Data dependency.
        Assert.Equal("Read", book.Status);
        Assert.Equal(5, book.Rating);
    }

    [Fact]
    public async Task GetSnapshotAsync_BookSeriesIdAndOrderProjectedFromFirstWork()
    {
        // Single-Work book in a numbered series: seriesId + seriesOrder
        // come from that Work. Multi-Work compendium takes the first
        // Work by Work.Id, matching the PrimaryAuthor convention.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var foundationSeries = new Series { Name = "Foundation", Type = SeriesType.Series, ExpectedCount = 7 };
            db.Series.Add(foundationSeries);
            await db.SaveChangesAsync();

            db.Books.Add(new Book
            {
                Title = "Foundation",
                Works =
                [
                    new Work
                    {
                        Title = "Foundation",
                        SeriesId = foundationSeries.Id,
                        SeriesOrder = 1,
                        WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }],
                    },
                ],
            });
            // Standalone book — no series.
            db.Books.Add(new Book
            {
                Title = "Nightfall",
                Works = [new Work { Title = "Nightfall", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var foundation = snapshot.Books.Single(b => b.Title == "Foundation");
        Assert.NotNull(foundation.SeriesId);
        Assert.Equal(1, foundation.SeriesOrder);

        var nightfall = snapshot.Books.Single(b => b.Title == "Nightfall");
        Assert.Null(nightfall.SeriesId);
        Assert.Null(nightfall.SeriesOrder);
    }

    [Fact]
    public async Task GetSnapshotAsync_InterquelProjectsFlooredOrderAndDisplayLabel()
    {
        // An interquel work (SeriesOrder floored to 4 + SeriesOrderDisplay
        // "4.5") ships BOTH the int (for sort + gap math) and the label so
        // mobile can render "4.5" and exclude it from numbered-slot ownership.
        using (var db = _factory.CreateDbContext())
        {
            var sanderson = new Author { Name = "Brandon Sanderson" };
            db.Authors.Add(sanderson);
            var stormlight = new Series { Name = "The Stormlight Archive", Type = SeriesType.Series, ExpectedCount = 5 };
            db.Series.Add(stormlight);
            await db.SaveChangesAsync();

            db.Books.Add(new Book
            {
                Title = "Edgedancer",
                Works =
                [
                    new Work
                    {
                        Title = "Edgedancer",
                        SeriesId = stormlight.Id,
                        SeriesOrder = 4,
                        SeriesOrderDisplay = "4.5",
                        WorkAuthors = [new WorkAuthor { Author = sanderson, Order = 0 }],
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var edgedancer = snapshot.Books.Single(b => b.Title == "Edgedancer");
        Assert.Equal(4, edgedancer.SeriesOrder);
        Assert.Equal("4.5", edgedancer.SeriesOrderDisplay);
    }

    [Fact]
    public async Task GetSnapshotAsync_SeriesListIncludesAllSeries_EvenEmptyOnes()
    {
        // Series list is always full-listed (changed from "only series
        // referenced by Books" in the PR that added delta sync). The
        // rationale is correctness on delta refreshes: a Series rename
        // that doesn't happen to bump any owning Books (interceptor
        // doesn't propagate Series → Book) wouldn't surface to clients
        // on a `?since=` delta if Series were filtered. Cost is a few
        // KB on the wire for an empty-Series row.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var foundation = new Series { Name = "Foundation", Type = SeriesType.Series, ExpectedCount = 7 };
            var emptySeries = new Series { Name = "Empire of Light", Type = SeriesType.Series, ExpectedCount = 3 };
            db.Series.AddRange(foundation, emptySeries);
            await db.SaveChangesAsync();

            db.Books.Add(new Book
            {
                Title = "Foundation",
                Works = [new Work
                {
                    Title = "Foundation",
                    SeriesId = foundation.Id,
                    SeriesOrder = 1,
                    WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }],
                }],
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        Assert.Equal(2, snapshot.Series.Count);
        Assert.Contains(snapshot.Series, s => s.Name == "Foundation" && s.ExpectedCount == 7);
        Assert.Contains(snapshot.Series, s => s.Name == "Empire of Light" && s.ExpectedCount == 3);
    }

    // ---- Delta-sync (`?since=` filter + LatestUpdatedAt) ----

    [Fact]
    public async Task GetSnapshotAsync_NoSince_ReturnsAllBooks_AndLatestIsMaxUpdatedAt()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.AddRange(
                new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] },
                new Book { Title = "I, Robot", Works = [new Work { Title = "I, Robot", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot(since: null);

        Assert.Equal(2, snapshot.Books.Count);
        // LatestUpdatedAt = max Book.UpdatedAt in the result. Both
        // books were just saved so both have post-creation stamps;
        // the max is non-default and not the syncedAt sentinel.
        Assert.True(snapshot.LatestUpdatedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(snapshot.LatestUpdatedAt <= snapshot.SyncedAt);
    }

    [Fact]
    public async Task GetSnapshotAsync_SinceInFuture_ReturnsNoBooks_AndLatestIsSince()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.Add(new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var farFuture = DateTime.UtcNow.AddDays(1);
        var snapshot = await GetSnapshot(since: farFuture);

        Assert.Empty(snapshot.Books);
        // Empty delta echoes back the supplied since — clients keep
        // polling with the same token until something changes.
        Assert.Equal(farFuture, snapshot.LatestUpdatedAt);
    }

    [Fact]
    public async Task GetSnapshotAsync_SinceMidway_ReturnsOnlyBooksBumpedAfter()
    {
        DateTime mid;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.Add(new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        // Datetime2 has 100ns precision; a small delay guarantees the
        // mid stamp falls between the two saves with no risk of
        // boundary races.
        await Task.Delay(20);
        mid = DateTime.UtcNow;
        await Task.Delay(20);

        using (var db = _factory.CreateDbContext())
        {
            var asimov = db.Authors.Single();
            db.Books.Add(new Book { Title = "I, Robot", Works = [new Work { Title = "I, Robot", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var delta = await GetSnapshot(since: mid);

        var book = Assert.Single(delta.Books);
        Assert.Equal("I, Robot", book.Title);
        Assert.True(delta.LatestUpdatedAt > mid);
    }

    [Fact]
    public async Task GetSnapshotAsync_DeltaResponse_StillFullsAuthorsAndSeries()
    {
        // Even on a delta call, Authors + Series ship in full —
        // they're small payloads and the client needs the full set
        // for joins/lookups against books it has locally cached.
        DateTime mid;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Series.Add(new Series { Name = "Foundation", Type = SeriesType.Series, ExpectedCount = 7 });
            db.Books.Add(new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        await Task.Delay(20);
        mid = DateTime.UtcNow;
        await Task.Delay(20);

        // No book changes after mid — delta should be empty for books,
        // but Authors + Series still full-listed.
        var delta = await GetSnapshot(since: mid);

        Assert.Empty(delta.Books);
        Assert.NotEmpty(delta.Authors);
        Assert.Contains(delta.Authors, a => a.Name == "Isaac Asimov");
        Assert.NotEmpty(delta.Series);
        Assert.Contains(delta.Series, s => s.Name == "Foundation");
    }

    // ---- Enriched detail (Editions + Works) ----

    [Fact]
    public async Task GetSnapshotAsync_ProjectsEditionsWithFormatAndCoverUrl()
    {
        using (var db = _factory.CreateDbContext())
        {
            var clarke = new Author { Name = "Arthur C. Clarke" };
            db.Authors.Add(clarke);
            db.Books.Add(new Book
            {
                Title = "Rendezvous with Rama",
                Works = [new Work { Title = "Rendezvous with Rama", WorkAuthors = [new WorkAuthor { Author = clarke, Order = 0 }] }],
                Editions =
                [
                    new Edition { Isbn = "9780553287899", Format = BookFormat.MassMarketPaperback, CoverUrl = "https://covers.example/mm.jpg" },
                    new Edition { Isbn = "9780575094192", Format = BookFormat.TradePaperback, CoverUrl = null },
                    // No-ISBN edition still shipped (Format + maybe a
                    // CoverUrl) so the enhanced ScanPage view doesn't
                    // hide them.
                    new Edition { Isbn = null, Format = BookFormat.Hardcover, CoverUrl = null },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var book = Assert.Single(snapshot.Books);

        Assert.NotNull(book.Editions);
        Assert.Equal(3, book.Editions!.Count);
        // Format serialised as enum name string (DTO is data-project free).
        Assert.Contains(book.Editions, e => e.Format == "MassMarketPaperback" && e.Isbn == "9780553287899");
        Assert.Contains(book.Editions, e => e.Format == "TradePaperback" && e.Isbn == "9780575094192");
        Assert.Contains(book.Editions, e => e.Format == "Hardcover" && e.Isbn is null);
        // CoverUrl preserved through the projection.
        Assert.Equal("https://covers.example/mm.jpg",
            book.Editions.First(e => e.Format == "MassMarketPaperback").CoverUrl);
    }

    [Fact]
    public async Task GetSnapshotAsync_ProjectsWorksWithPerWorkPrimaryAuthor()
    {
        // Compendium with multiple Works, each by a different author —
        // the per-Work PrimaryAuthor must reflect THAT Work's lowest-
        // Order WorkAuthor, not the Book-level rollup.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            db.Authors.AddRange(asimov, king);
            db.Books.Add(new Book
            {
                Title = "The Funhouse",
                Works =
                [
                    new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                    new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var book = Assert.Single(snapshot.Books);

        Assert.NotNull(book.Works);
        Assert.Equal(2, book.Works!.Count);
        Assert.Contains(book.Works, w => w.Title == "Asimov story" && w.PrimaryAuthor == "Isaac Asimov");
        Assert.Contains(book.Works, w => w.Title == "King story" && w.PrimaryAuthor == "Stephen King");
    }

    [Fact]
    public async Task GetSnapshotAsync_EditorOnlyWork_PrimaryAuthor_FallsBackToEditorWithRoleSuffix()
    {
        // Dictionary / Oxford Companion case — Work has an editor but no
        // author. BookSnapshot.PrimaryAuthor and WorkSnapshot.PrimaryAuthor
        // should both display the editor name with role suffix (not
        // "(unknown)") so the by-line conveys "edited by X".
        using (var db = _factory.CreateDbContext())
        {
            var editor = new Author { Name = "Catherine Soanes" };
            db.Authors.Add(editor);
            db.Books.Add(new Book
            {
                Title = "Concise Oxford English Dictionary",
                Works =
                [
                    new Work
                    {
                        Title = "Concise Oxford English Dictionary",
                        WorkAuthors = [new WorkAuthor { Author = editor, Order = 0, Role = AuthorRole.Editor }]
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();
        var book = Assert.Single(snapshot.Books);

        Assert.Equal("Catherine Soanes (editor)", book.PrimaryAuthor);
        var work = Assert.Single(book.Works!);
        Assert.Equal("Catherine Soanes (editor)", work.PrimaryAuthor);
        Assert.Equal([new AuthorContribution("Catherine Soanes", "Editor")], book.AllAuthors);
    }

    // ---- Soft-delete + deletedIds tombstones ----

    [Fact]
    public async Task GetSnapshotAsync_SoftDeletedBook_ExcludedFromBooksList()
    {
        // The global HasQueryFilter on Book hides soft-deleted rows
        // from every normal query — including the snapshot's Books
        // projection. The husk row stays in the DB only to power
        // tombstone emission on the delta path.
        int bookIdToKeep, bookIdToDelete;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var keeper = new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] };
            var doomed = new Book { Title = "Doomed", Works = [new Work { Title = "Doomed", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] };
            db.Books.AddRange(keeper, doomed);
            await db.SaveChangesAsync();
            bookIdToKeep = keeper.Id;
            bookIdToDelete = doomed.Id;
        }

        using (var db = _factory.CreateDbContext())
        {
            var toDelete = await db.Books.FirstAsync(b => b.Id == bookIdToDelete);
            toDelete.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot();

        var surviving = Assert.Single(snapshot.Books);
        Assert.Equal(bookIdToKeep, surviving.Id);
        Assert.Empty(snapshot.DeletedIds ?? []);
    }

    [Fact]
    public async Task GetSnapshotAsync_FullSnapshot_ReturnsEmptyDeletedIds()
    {
        // No `since` ⇒ client is doing a fresh load and has no local
        // rows to tombstone. Always return empty (not null) so client
        // code can iterate without null-checks.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var dead = new Book { Title = "Doomed", Works = [new Work { Title = "Doomed", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] };
            db.Books.Add(dead);
            await db.SaveChangesAsync();
            dead.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var snapshot = await GetSnapshot(since: null);

        Assert.NotNull(snapshot.DeletedIds);
        Assert.Empty(snapshot.DeletedIds);
    }

    [Fact]
    public async Task GetSnapshotAsync_DeltaWithTombstone_EmitsIdAndAdvancesLatest()
    {
        // The killer case: client did a full sync at T0, then a Book
        // was soft-deleted at T1, client refreshes with since=T0. The
        // tombstone must surface in deletedIds AND LatestUpdatedAt must
        // advance past T1 — otherwise the client sends the same `since`
        // forever and infinite-loops the tombstone.
        int doomedId;
        DateTime midpoint;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.Add(new Book { Title = "Foundation", Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] });
            var doomed = new Book { Title = "Doomed", Works = [new Work { Title = "Doomed", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] };
            db.Books.Add(doomed);
            await db.SaveChangesAsync();
            doomedId = doomed.Id;
        }

        await Task.Delay(20);
        midpoint = DateTime.UtcNow;
        await Task.Delay(20);

        using (var db = _factory.CreateDbContext())
        {
            var doomed = await db.Books.FirstAsync(b => b.Id == doomedId);
            doomed.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var delta = await GetSnapshot(since: midpoint);

        // Live Books with UpdatedAt > midpoint — none (the surviving
        // book hasn't changed since its creation, which was before
        // midpoint). Tombstones — exactly the doomed one.
        Assert.Empty(delta.Books);
        Assert.NotNull(delta.DeletedIds);
        var tombstoneId = Assert.Single(delta.DeletedIds);
        Assert.Equal(doomedId, tombstoneId);

        // Advance-past-tombstone invariant: next-token must move
        // forward so the client doesn't refetch the same tombstone.
        Assert.True(delta.LatestUpdatedAt > midpoint,
            $"LatestUpdatedAt must advance past tombstone's DeletedAt. midpoint={midpoint:O}, latest={delta.LatestUpdatedAt:O}");
    }

    [Fact]
    public async Task GetSnapshotAsync_DeltaWithOldTombstone_OmitsIt()
    {
        // Tombstone-already-seen: a Book was soft-deleted at T1, client
        // synced past T1, client now polls with since>T1. The
        // tombstone is older than `since` and must NOT reappear — the
        // client has already dropped it.
        int doomedId;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var doomed = new Book { Title = "Doomed", Works = [new Work { Title = "Doomed", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }] };
            db.Books.Add(doomed);
            await db.SaveChangesAsync();
            doomedId = doomed.Id;
            doomed.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await Task.Delay(20);
        var afterDelete = DateTime.UtcNow;

        var delta = await GetSnapshot(since: afterDelete);

        Assert.NotNull(delta.DeletedIds);
        Assert.Empty(delta.DeletedIds);
    }
}
