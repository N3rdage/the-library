using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests for Edition's and Copy's write methods: no EF, no
// container.
[Trait("Category", TestCategories.Unit)]
public class EditionAggregateTests
{
    [Fact]
    public void ReplaceCoverUrl_setsOnlyTheUrl_andKeepsIsUserSupplied()
    {
        var edition = TestEditions.Edition(coverUrl: "https://up/a.jpg", isUserSupplied: true);

        edition.ReplaceCoverUrl("https://blob/a.jpg");

        Assert.Equal("https://blob/a.jpg", edition.CoverUrl);
        Assert.True(edition.IsUserSupplied);
    }

    [Fact]
    public void SetCover_setsUrlAndUserSupplied()
    {
        var edition = TestEditions.Edition(coverUrl: "https://up/a.jpg");

        edition.SetCover("https://blob/mine.jpg", userSupplied: true);

        Assert.Equal("https://blob/mine.jpg", edition.CoverUrl);
        Assert.True(edition.IsUserSupplied);
    }

    [Fact]
    public void ChangeFormat_setsFormat()
    {
        var edition = TestEditions.Edition(format: BookFormat.TradePaperback);

        edition.ChangeFormat(BookFormat.Hardcover);

        Assert.Equal(BookFormat.Hardcover, edition.Format);
    }

    [Fact]
    public void ReassignPublisher_setsTheKey()
    {
        var edition = TestEditions.Edition(publisherId: 3);

        edition.ReassignPublisher(7);

        Assert.Equal(7, edition.PublisherId);
    }

    [Fact]
    public void MoveToBook_setsTheKey()
    {
        var edition = TestEditions.Edition(bookId: 1);

        edition.MoveToBook(2);

        Assert.Equal(2, edition.BookId);
    }

    [Fact]
    public void FillBlanksFrom_fillsEveryEmptyField_andCountsThem()
    {
        var winner = TestEditions.Edition(format: BookFormat.Hardcover);
        var loser = TestEditions.Edition(
            format: BookFormat.MassMarketPaperback,
            isbn: "9780000000099",
            datePrinted: new DateOnly(1984, 6, 1),
            datePrintedPrecision: DatePrecision.Month,
            coverUrl: "https://example.com/cover.jpg",
            publisherId: 5);

        var filled = winner.FillBlanksFrom(loser);

        Assert.Equal(4, filled);
        Assert.Equal("9780000000099", winner.Isbn);
        Assert.Equal(new DateOnly(1984, 6, 1), winner.DatePrinted);
        Assert.Equal(DatePrecision.Month, winner.DatePrintedPrecision);
        Assert.Equal("https://example.com/cover.jpg", winner.CoverUrl);
        Assert.Equal(5, winner.PublisherId);
        // Format is not a blank-fill field.
        Assert.Equal(BookFormat.Hardcover, winner.Format);
    }

    [Fact]
    public void FillBlanksFrom_keepsEveryPopulatedField()
    {
        var winner = TestEditions.Edition(
            isbn: "9780000000111",
            datePrinted: new DateOnly(1990, 1, 1),
            datePrintedPrecision: DatePrecision.Year,
            coverUrl: "https://example.com/winner.jpg",
            publisherId: 1);
        var loser = TestEditions.Edition(
            isbn: "9780000000222",
            datePrinted: new DateOnly(1985, 1, 1),
            datePrintedPrecision: DatePrecision.Day,
            coverUrl: "https://example.com/loser.jpg",
            publisherId: 2);

        var filled = winner.FillBlanksFrom(loser);

        Assert.Equal(0, filled);
        Assert.Equal("9780000000111", winner.Isbn);
        Assert.Equal(new DateOnly(1990, 1, 1), winner.DatePrinted);
        Assert.Equal(DatePrecision.Year, winner.DatePrintedPrecision);
        Assert.Equal("https://example.com/winner.jpg", winner.CoverUrl);
        Assert.Equal(1, winner.PublisherId);
    }

    [Fact]
    public void FillBlanksFrom_treatsWhitespaceCoverAndIsbnAsEmpty()
    {
        var winner = TestEditions.Edition(isbn: " ", coverUrl: " ");
        var loser = TestEditions.Edition(isbn: "9780000000099", coverUrl: "https://example.com/c.jpg");

        var filled = winner.FillBlanksFrom(loser);

        Assert.Equal(2, filled);
        Assert.Equal("9780000000099", winner.Isbn);
        Assert.Equal("https://example.com/c.jpg", winner.CoverUrl);
    }

    [Fact]
    public void AdoptCopiesFrom_repointsEachCopy_andEmptiesTheOther()
    {
        var winner = TestEditions.Edition(copies: [TestEditions.Copy()]);
        winner.Id = 10;
        var loser = TestEditions.Edition(copies:
        [
            TestEditions.Copy(condition: BookCondition.Fair, notes: "signed", editionId: 20),
            TestEditions.Copy(editionId: 20),
        ]);
        loser.Id = 20;
        var moving = loser.Copies.ToList();

        var moved = winner.AdoptCopiesFrom(loser);

        Assert.Equal(2, moved);
        Assert.Empty(loser.Copies);
        Assert.All(moving, c => Assert.Equal(10, c.EditionId));
        // The copies keep their own fields; the winner's collection is not
        // touched (EF fixes it up from the keys on save).
        Assert.Equal(BookCondition.Fair, moving[0].Condition);
        Assert.Equal("signed", moving[0].Notes);
        Assert.Single(winner.Copies);
    }

    [Fact]
    public void AdoptCopiesFrom_noCopies_movesNothing()
    {
        var winner = TestEditions.Edition();
        var loser = TestEditions.Edition();

        Assert.Equal(0, winner.AdoptCopiesFrom(loser));
    }
}
