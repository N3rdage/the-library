using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Data;

// BookUpdatedAtInterceptor runs at SaveChangesAsync time on every
// DbContext created via TestDbContextFactory (matches production
// wiring in ProgramSetup.cs). These tests verify it stamps
// Book.UpdatedAt on every aggregate-modification shape that the
// delta-sync `?since=<token>` query depends on.
//
// Real SQL, not InMemory — datetime2 round-trips through the DB,
// catching any UTC-vs-Local mismatch or precision rounding that
// would break the > since comparison.
[Trait("Category", TestCategories.Integration)]
public class BookUpdatedAtInterceptorTests
{
    private readonly TestDbContextFactory _factory = new();

    // SQL Server datetime2 has 100ns precision and DateTime.UtcNow has
    // the same; two successive saves on a fast machine *can* land in
    // the same tick, which would make our "did UpdatedAt move?"
    // assertions fail. A small delay between the seed save and the
    // mutation save eliminates the race without slowing the suite
    // meaningfully (20ms * 6 tests = ~0.1s overhead).
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task DirectBookEdit_BumpsUpdatedAt()
    {
        int bookId;
        DateTime initial;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var book = new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            initial = book.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.FirstAsync(b => b.Id == bookId);
            book.Title = "Foundation (revised)";
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var bumped = await db.Books.FirstAsync(b => b.Id == bookId);
            Assert.True(bumped.UpdatedAt > initial,
                $"Expected UpdatedAt to bump after Book edit. initial={initial:O}, current={bumped.UpdatedAt:O}");
        }
    }

    [Fact]
    public async Task EditionEdit_BumpsOwningBookUpdatedAt()
    {
        int bookId;
        DateTime initial;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var book = new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780553293357" }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            initial = book.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            var edition = await db.Editions.Include(e => e.Book).FirstAsync();
            edition.Isbn = "9780553293358";
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var bumped = await db.Books.FirstAsync(b => b.Id == bookId);
            Assert.True(bumped.UpdatedAt > initial,
                $"Expected UpdatedAt to bump after Edition edit. initial={initial:O}, current={bumped.UpdatedAt:O}");
        }
    }

    [Fact]
    public async Task CopyEdit_BumpsOwningBookUpdatedAt()
    {
        int bookId;
        DateTime initial;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var book = new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
                Editions = [new Edition
                {
                    Isbn = "9780553293357",
                    Copies = [new Copy { Condition = BookCondition.Good }],
                }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            initial = book.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            var copy = await db.Copies
                .Include(c => c.Edition).ThenInclude(e => e.Book)
                .FirstAsync();
            copy.Condition = BookCondition.VeryGood;
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var bumped = await db.Books.FirstAsync(b => b.Id == bookId);
            Assert.True(bumped.UpdatedAt > initial,
                $"Expected UpdatedAt to bump after Copy edit. initial={initial:O}, current={bumped.UpdatedAt:O}");
        }
    }

    [Fact]
    public async Task WorkEdit_BumpsEveryOwningBookUpdatedAt()
    {
        // Work ↔ Book is M:N. Editing a Work that's attached to multiple
        // Books must bump every owning Book — e.g. "Call of Cthulhu"
        // story attached to three different Lovecraft anthologies.
        int bookAId, bookBId;
        DateTime initialA, initialB;
        using (var db = _factory.CreateDbContext())
        {
            var lovecraft = new Author { Name = "H.P. Lovecraft" };
            db.Authors.Add(lovecraft);
            var sharedWork = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = lovecraft, Order = 0 }],
            };
            var bookA = new Book { Title = "Anthology A", Works = [sharedWork] };
            var bookB = new Book { Title = "Anthology B", Works = [sharedWork] };
            db.Books.AddRange(bookA, bookB);
            await db.SaveChangesAsync();
            bookAId = bookA.Id;
            bookBId = bookB.Id;
            initialA = bookA.UpdatedAt;
            initialB = bookB.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            var work = await db.Works.Include(w => w.Books).FirstAsync();
            work.Title = "The Call of Cthulhu (revised)";
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var a = await db.Books.FirstAsync(b => b.Id == bookAId);
            var b = await db.Books.FirstAsync(b => b.Id == bookBId);
            Assert.True(a.UpdatedAt > initialA, "Book A UpdatedAt should bump");
            Assert.True(b.UpdatedAt > initialB, "Book B UpdatedAt should bump");
        }
    }

    [Fact]
    public async Task WorkAuthorChange_BumpsOwningBookUpdatedAt()
    {
        // Reordering a Work's authors (WorkAuthor.Order change) is a
        // bumpable aggregate change — the snapshot's PrimaryAuthor /
        // AllAuthors derivation depends on the order.
        int bookId;
        DateTime initial;
        using (var db = _factory.CreateDbContext())
        {
            var preston = new Author { Name = "Douglas Preston" };
            var child = new Author { Name = "Lincoln Child" };
            db.Authors.AddRange(preston, child);
            var book = new Book
            {
                Title = "Relic",
                Works =
                [
                    new Work
                    {
                        Title = "Relic",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = preston, Order = 0 },
                            new WorkAuthor { Author = child, Order = 1 },
                        ],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            initial = book.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            // Swap the two authors' Order — Child becomes lead.
            var workAuthors = await db.WorkAuthors.ToListAsync();
            var preston = workAuthors.Single(wa => wa.Order == 0);
            var child = workAuthors.Single(wa => wa.Order == 1);
            preston.Order = 1;
            child.Order = 0;
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var bumped = await db.Books.FirstAsync(b => b.Id == bookId);
            Assert.True(bumped.UpdatedAt > initial,
                $"Expected UpdatedAt to bump after WorkAuthor reorder. initial={initial:O}, current={bumped.UpdatedAt:O}");
        }
    }

    [Fact]
    public async Task TagAttachedToBook_BumpsBookUpdatedAt()
    {
        // Adding a tag to a Book is an aggregate change. (Tag.Name
        // rename is the other tag-related bump shape, but the join-
        // table mutation is the common case from the Library page.)
        int bookId;
        DateTime initial;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var book = new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
            };
            db.Books.Add(book);
            db.Tags.Add(new Tag { Name = "favourites" });
            await db.SaveChangesAsync();
            bookId = book.Id;
            initial = book.UpdatedAt;
        }

        await Task.Delay(Tick);

        using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.Include(b => b.Tags).FirstAsync(b => b.Id == bookId);
            var tag = await db.Tags.SingleAsync(t => t.Name == "favourites");
            book.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            var bumped = await db.Books.FirstAsync(b => b.Id == bookId);
            Assert.True(bumped.UpdatedAt > initial,
                $"Expected UpdatedAt to bump after Tag attach. initial={initial:O}, current={bumped.UpdatedAt:O}");
        }
    }

    [Fact]
    public async Task UpdatedAt_AndDeletedAt_ReadBack_WithKindUtc()
    {
        // Regression test for the cross-timezone delta-sync bug
        // diagnosed 2026-05-14: SQL Server's datetime2 doesn't store
        // DateTime.Kind, so without an EF value converter, EF returns
        // Kind=Unspecified on read. System.Text.Json then serialises
        // Unspecified timestamps WITHOUT the trailing "Z" — non-UTC
        // clients parse them as Local and their .ToUniversalTime()
        // shifts the watermark by the client's TZ offset on the next
        // ?since= call. The fix is the HasConversion(...) stamps in
        // BookTrackerDbContext.OnModelCreating; this test locks the
        // contract so a future refactor can't accidentally remove the
        // converter without a CI signal.
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            var book = new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
                DeletedAt = DateTime.UtcNow, // populate so we can assert Kind on the nullable property too
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        using (var db = _factory.CreateDbContext())
        {
            // IgnoreQueryFilters so we get the tombstoned husk back —
            // otherwise the global filter would hide it.
            var reread = await db.Books.IgnoreQueryFilters()
                .FirstAsync(b => b.Id == bookId);
            Assert.Equal(DateTimeKind.Utc, reread.UpdatedAt.Kind);
            Assert.NotNull(reread.DeletedAt);
            Assert.Equal(DateTimeKind.Utc, reread.DeletedAt!.Value.Kind);
        }
    }
}
