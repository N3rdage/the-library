using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests — no EF, no container. Exercise the invariants that
// moved onto the Book/Edition/Copy aggregate in the back-end refactor pilot
// (docs/BACKEND-REFACTOR-DESIGN.md). Fast (<10ms each), no Docker needed.
[Trait("Category", TestCategories.Unit)]
public class BookAggregateTests
{
    [Fact]
    public void Rate_validValue_sets()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.Rate(4);
        Assert.Equal(4, book.Rating);
    }

    [Fact]
    public void AttachWork_appendsWithIncreasingOrder()
    {
        var book = Book.Create("Anthology", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var first = new Work { Title = "First" };
        var second = new Work { Title = "Second" };
        var third = new Work { Title = "Third" };

        Assert.True(book.AttachWork(first));
        Assert.True(book.AttachWork(second));
        Assert.True(book.AttachWork(third));

        Assert.Equal([0, 1, 2], book.BookWorks.Select(bw => bw.Order).ToArray());
        Assert.Equal(["First", "Second", "Third"], book.BookWorks.OrderBy(bw => bw.Order).Select(bw => bw.Work.Title).ToArray());
        // Both ends of the graph are wired so the aggregate stays consistent in-memory.
        Assert.Same(book, first.BookWorks.Single().Book);
    }

    [Fact]
    public void AttachWork_alreadyAttached_isNoOp()
    {
        var book = Book.Create("Anthology", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var work = new Work { Title = "Only" };

        Assert.True(book.AttachWork(work));
        Assert.False(book.AttachWork(work));      // second attach → no-op
        Assert.Single(book.BookWorks);
        Assert.Equal(0, book.BookWorks.Single().Order);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Rate_outOfRange_throws(int rating)
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        Assert.Throws<DomainRuleException>(() => book.Rate(rating));
    }

    [Fact]
    public void UpdateDetails_blankTitle_throws()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        Assert.Throws<DomainRuleException>(() => book.UpdateDetails("   ", BookCategory.Fiction, null));
    }

    [Fact]
    public void UpdateDetails_trimsTitleAndCover_andSetsCategory()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.UpdateDetails("  Dune  ", BookCategory.NonFiction, "  http://c  ");
        Assert.Equal("Dune", book.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
        Assert.Equal("http://c", book.DefaultCoverArtUrl);
    }

    [Fact]
    public void UpdateDetails_blankCover_setsNull()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, "old");
        book.UpdateDetails("Dune", BookCategory.Fiction, "   ");
        Assert.Null(book.DefaultCoverArtUrl);
    }

    [Fact]
    public void SetCover_trimsUrl_andLeavesOtherDetailsAlone()
    {
        var book = Book.Create("Dune", BookCategory.NonFiction, BookStatus.Reading, 3, "note", "old");
        book.SetCover("  https://blob/covers/books/1.jpg  ");

        Assert.Equal("https://blob/covers/books/1.jpg", book.DefaultCoverArtUrl);
        Assert.Equal("Dune", book.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
        Assert.Equal(BookStatus.Reading, book.Status);
        Assert.Equal(3, book.Rating);
        Assert.Equal("note", book.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCover_blank_setsNull(string? url)
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, "old");
        book.SetCover(url);
        Assert.Null(book.DefaultCoverArtUrl);
    }

    [Fact]
    public void UpdateNotes_trimsThenNullsBlank()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.UpdateNotes("  hi  ");
        Assert.Equal("hi", book.Notes);
        book.UpdateNotes("   ");
        Assert.Null(book.Notes);
    }

    [Fact]
    public void MarkRead_setsStatusRatingAndNotes_inOneGesture()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.MarkRead(5, "  loved it  ");

        Assert.Equal(BookStatus.Read, book.Status);
        Assert.Equal(5, book.Rating);
        Assert.Equal("loved it", book.Notes);
    }

    [Fact]
    public void MarkRead_nullNotes_leavesExistingNotesIntact()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, "keep me", null);
        book.MarkRead(4, null);

        Assert.Equal(BookStatus.Read, book.Status);
        Assert.Equal(4, book.Rating);
        Assert.Equal("keep me", book.Notes); // null notes = no change, not a wipe
    }

    [Fact]
    public void MarkRead_blankNotes_clearsExistingNotes()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, "old", null);
        book.MarkRead(3, "   ");
        Assert.Null(book.Notes);
    }

    [Fact]
    public void MarkRead_invalidRating_throws_andLeavesBookUntouched()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);

        Assert.Throws<DomainRuleException>(() => book.MarkRead(7, "note"));

        Assert.Equal(BookStatus.Unread, book.Status); // rating validated first → nothing applied
        Assert.Null(book.Notes);
    }

    [Fact]
    public void AddEdition_createsEditionWithSingleFirstCopy_andTrimsIsbn()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var edition = book.AddEdition("  978-1  ", BookFormat.Hardcover, null, null, DatePrecision.Day, null, null, BookCondition.Fine);

        Assert.Single(book.Editions);
        Assert.Same(edition, book.Editions[0]);
        Assert.Equal("978-1", edition.Isbn);
        Assert.Single(edition.Copies);
        Assert.Equal(BookCondition.Fine, edition.Copies[0].Condition);
    }

    [Fact]
    public void AddEdition_setsEditionNumber()
    {
        var book = Book.Create("Joy of Cooking", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var edition = book.AddEdition(null, BookFormat.Hardcover, 3, null, DatePrecision.Day, null, null, BookCondition.Good);

        Assert.Equal(3, edition.EditionNumber);
    }

    [Fact]
    public void AddEdition_nullEditionNumber_leavesItNull()
    {
        var book = Book.Create("Foundation", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var edition = book.AddEdition(null, BookFormat.MassMarketPaperback, null, null, DatePrecision.Day, null, null, BookCondition.Good);

        Assert.Null(edition.EditionNumber);
    }

    [Fact]
    public void AddEdition_isbnIsTrimmedOnly_notNormalised()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var edition = book.AddEdition(" 978-0-345-39180-3 ", BookFormat.Hardcover, null, null, DatePrecision.Day, null, null, BookCondition.Good);

        Assert.Equal("978-0-345-39180-3", edition.Isbn);
    }

    [Fact]
    public void Create_setsFields_trimsTitle_andNullsBlankNotesAndCover()
    {
        var book = Book.Create("  Mort  ", BookCategory.NonFiction, BookStatus.Reading, 4, "   ", "   ");

        Assert.Equal("Mort", book.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
        Assert.Equal(BookStatus.Reading, book.Status);
        Assert.Equal(4, book.Rating);
        Assert.Null(book.Notes);
        Assert.Null(book.DefaultCoverArtUrl);
        Assert.Empty(book.Editions);
        Assert.Empty(book.BookWorks);
        Assert.Null(book.SeriesId);
    }

    [Fact]
    public void Create_trimsNotesAndCover()
    {
        var book = Book.Create("Mort", BookCategory.Fiction, BookStatus.Unread, 0, "  signed  ", "  https://c/x.jpg  ");

        Assert.Equal("signed", book.Notes);
        Assert.Equal("https://c/x.jpg", book.DefaultCoverArtUrl);
    }

    [Fact]
    public void Create_stampsDateAddedAtCreation()
    {
        var before = DateTime.UtcNow;
        var book = Book.Create("Mort", BookCategory.Fiction, BookStatus.Unread, 0, null, null);

        Assert.InRange(book.DateAdded, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_blankTitle_throws(string title)
    {
        Assert.Throws<DomainRuleException>(() =>
            Book.Create(title, BookCategory.Fiction, BookStatus.Unread, 0, null, null));
    }

    [Fact]
    public void RemoveCopy_notLast_keepsEdition()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        var edition = new Edition { Id = 1, Copies = { new Copy { Id = 10 }, new Copy { Id = 11 } } };
        book.Editions.Add(edition);

        book.RemoveCopy(10);

        Assert.Single(book.Editions);
        Assert.Single(edition.Copies);
        Assert.Equal(11, edition.Copies[0].Id);
    }

    [Fact]
    public void RemoveCopy_last_removesEdition()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.Editions.Add(new Edition { Id = 1, Copies = { new Copy { Id = 10 } } });

        book.RemoveCopy(10);

        Assert.Empty(book.Editions);
    }

    [Fact]
    public void RemoveCopy_unknownCopy_throws()
    {
        var book = Book.Create("X", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
        book.Editions.Add(new Edition { Id = 1, Copies = { new Copy { Id = 10 } } });

        Assert.Throws<DomainRuleException>(() => book.RemoveCopy(999));
    }

    [Fact]
    public void SoftDelete_clearsTagsAndEditionsAndStampsTombstone_butLeavesWorksToTheHandler()
    {
        var book = TestBooks.Book(
            "X",
            works: [new Work { Title = "W" }],
            tags: [new Tag { Name = "t" }],
            editions: [new Edition { Copies = { new Copy() } }]);

        book.SoftDelete();

        Assert.Empty(book.Tags);
        Assert.Empty(book.Editions);
        Assert.NotNull(book.DeletedAt);
        // Works are a shared, ref-counted aggregate — SoftDelete leaves them; the
        // DeleteBookHandler detaches and orphan-deletes them.
        Assert.Single(book.Works);
    }

    [Fact]
    public void Edition_AddCopy_appendsAndTrimsNotes()
    {
        var edition = new Edition();
        var copy = edition.AddCopy(BookCondition.Good, null, "  note  ");

        Assert.Single(edition.Copies);
        Assert.Equal("note", copy.Notes);
    }

    [Fact]
    public void Copy_UpdateDetails_setsFields_andNullsBlankNotes()
    {
        var copy = TestEditions.Copy(condition: BookCondition.Good, notes: "old");
        copy.UpdateDetails(BookCondition.Poor, null, "  ");

        Assert.Equal(BookCondition.Poor, copy.Condition);
        Assert.Null(copy.Notes);
    }
}
