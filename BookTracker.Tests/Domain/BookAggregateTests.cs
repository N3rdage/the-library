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
        var book = new Book { Title = "X" };
        book.Rate(4);
        Assert.Equal(4, book.Rating);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Rate_outOfRange_throws(int rating)
    {
        var book = new Book { Title = "X" };
        Assert.Throws<DomainRuleException>(() => book.Rate(rating));
    }

    [Fact]
    public void UpdateDetails_blankTitle_throws()
    {
        var book = new Book { Title = "X" };
        Assert.Throws<DomainRuleException>(() => book.UpdateDetails("   ", BookCategory.Fiction, null));
    }

    [Fact]
    public void UpdateDetails_trimsTitleAndCover_andSetsCategory()
    {
        var book = new Book { Title = "X" };
        book.UpdateDetails("  Dune  ", BookCategory.NonFiction, "  http://c  ");
        Assert.Equal("Dune", book.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
        Assert.Equal("http://c", book.DefaultCoverArtUrl);
    }

    [Fact]
    public void UpdateDetails_blankCover_setsNull()
    {
        var book = new Book { Title = "X", DefaultCoverArtUrl = "old" };
        book.UpdateDetails("Dune", BookCategory.Fiction, "   ");
        Assert.Null(book.DefaultCoverArtUrl);
    }

    [Fact]
    public void UpdateNotes_trimsThenNullsBlank()
    {
        var book = new Book { Title = "X" };
        book.UpdateNotes("  hi  ");
        Assert.Equal("hi", book.Notes);
        book.UpdateNotes("   ");
        Assert.Null(book.Notes);
    }

    [Fact]
    public void AddEdition_createsEditionWithSingleFirstCopy_andTrimsIsbn()
    {
        var book = new Book { Title = "X" };
        var edition = book.AddEdition("  978-1  ", BookFormat.Hardcover, null, DatePrecision.Day, null, null, BookCondition.Fine);

        Assert.Single(book.Editions);
        Assert.Same(edition, book.Editions[0]);
        Assert.Equal("978-1", edition.Isbn);
        Assert.Single(edition.Copies);
        Assert.Equal(BookCondition.Fine, edition.Copies[0].Condition);
    }

    [Fact]
    public void RemoveCopy_notLast_keepsEdition()
    {
        var book = new Book { Title = "X" };
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
        var book = new Book { Title = "X" };
        book.Editions.Add(new Edition { Id = 1, Copies = { new Copy { Id = 10 } } });

        book.RemoveCopy(10);

        Assert.Empty(book.Editions);
    }

    [Fact]
    public void RemoveCopy_unknownCopy_throws()
    {
        var book = new Book { Title = "X" };
        book.Editions.Add(new Edition { Id = 1, Copies = { new Copy { Id = 10 } } });

        Assert.Throws<DomainRuleException>(() => book.RemoveCopy(999));
    }

    [Fact]
    public void SoftDelete_clearsJoinsAndStampsTombstone()
    {
        var book = new Book
        {
            Title = "X",
            Works = { new Work { Title = "W" } },
            Tags = { new Tag { Name = "t" } },
        };

        book.SoftDelete();

        Assert.Empty(book.Works);
        Assert.Empty(book.Tags);
        Assert.NotNull(book.DeletedAt);
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
        var copy = new Copy { Condition = BookCondition.Good, Notes = "old" };
        copy.UpdateDetails(BookCondition.Poor, null, "  ");

        Assert.Equal(BookCondition.Poor, copy.Condition);
        Assert.Null(copy.Notes);
    }
}
