using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class BookPhotoReadingParserTests
{
    private const string Full = """
        {"readable": true,
         "title": {"value": "The Oxford Dictionary of Quotations", "confidence": 0.97},
         "subtitle": {"value": "Seventh Edition", "confidence": 0.8},
         "contributors": [
           {"name": "Elizabeth Knowles", "role": "Editor", "confidence": 0.9},
           {"name": "Jane Roe", "role": "translator", "confidence": 0.5}],
         "publisher": {"value": "Oxford University Press", "confidence": 0.7},
         "year": {"value": 2009, "confidence": 0.6},
         "edition_number": {"value": 7, "confidence": 0.85},
         "isbn": {"value": "978-0-19-923718-1", "confidence": 0.9},
         "reference_work": {"is_reference_work": true, "kind": "dictionary", "confidence": 0.92}}
        """;

    [Fact]
    public void WellFormed_ReadsEveryFieldWithItsConfidence()
    {
        var reading = SharedParsers.ParseBookPhotoReading(Full);

        Assert.NotNull(reading);
        Assert.Equal(new PhotoField<string>("The Oxford Dictionary of Quotations", 0.97), reading.Title);
        Assert.Equal(new PhotoField<string>("Seventh Edition", 0.8), reading.Subtitle);
        Assert.Equal(
            [new PhotoContributor("Elizabeth Knowles", AuthorRole.Editor, 0.9), new PhotoContributor("Jane Roe", AuthorRole.Translator, 0.5)],
            reading.Contributors);
        Assert.Equal(new PhotoField<string>("Oxford University Press", 0.7), reading.Publisher);
        Assert.Equal(new PhotoField<int>(2009, 0.6), reading.Year);
        Assert.Equal(new PhotoField<int>(7, 0.85), reading.EditionNumber);
        Assert.Equal(new PhotoField<string>("9780199237181", 0.9), reading.Isbn);
        Assert.Equal(new ReferenceWorkHint(true, "dictionary", 0.92), reading.ReferenceWork);
    }

    [Fact]
    public void Partial_MissingFieldsAreNullAndReferenceHintDefaultsToFalse()
    {
        var reading = SharedParsers.ParseBookPhotoReading("""{"title": {"value": "Dune", "confidence": 0.99}}""");

        Assert.NotNull(reading);
        Assert.Equal("Dune", reading.Title!.Value);
        Assert.Null(reading.Subtitle);
        Assert.Empty(reading.Contributors);
        Assert.Null(reading.Publisher);
        Assert.Null(reading.Year);
        Assert.Null(reading.EditionNumber);
        Assert.Null(reading.Isbn);
        Assert.Equal(new ReferenceWorkHint(false, null, 0), reading.ReferenceWork);
    }

    [Fact]
    public void ContributorsOnly_StillCountsAsUsable()
    {
        var reading = SharedParsers.ParseBookPhotoReading("""{"contributors": [{"name": "Ursula K. Le Guin", "role": "Author", "confidence": 0.9}]}""");

        Assert.NotNull(reading);
        Assert.Null(reading.Title);
        Assert.Single(reading.Contributors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I cannot read this image.")]
    [InlineData("{\"title\": {\"value\": \"Dun")]
    [InlineData("[1, 2]")]
    [InlineData("\"just a string\"")]
    [InlineData("{}")]
    [InlineData("""{"readable": false}""")]
    [InlineData("""{"readable": false, "title": {"value": "Ghost", "confidence": 0.9}}""")]
    public void Unusable_ReturnsNullWithoutThrowing(string response)
    {
        Assert.Null(SharedParsers.ParseBookPhotoReading(response));
    }

    [Fact]
    public void CodeFences_AreStripped()
    {
        var reading = SharedParsers.ParseBookPhotoReading("```json\n" + Full + "\n```");

        Assert.Equal("The Oxford Dictionary of Quotations", reading!.Title!.Value);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("978019923718")]
    [InlineData("97801992371XX")]
    [InlineData("X123456789")]
    public void BadIsbn_IsDropped(string isbn)
    {
        var reading = SharedParsers.ParseBookPhotoReading(
            $$$"""{"title": {"value": "T", "confidence": 1}, "isbn": {"value": "{{{isbn}}}", "confidence": 1}}""");

        Assert.Null(reading!.Isbn);
    }

    [Fact]
    public void Isbn10WithTrailingX_IsKeptUppercased()
    {
        var reading = SharedParsers.ParseBookPhotoReading(
            """{"title": {"value": "T", "confidence": 1}, "isbn": {"value": "0-8044-2957-x", "confidence": 1}}""");

        Assert.Equal("080442957X", reading!.Isbn!.Value);
    }

    [Fact]
    public void Confidences_AreClampedToZeroToOne()
    {
        var reading = SharedParsers.ParseBookPhotoReading("""
            {"title": {"value": "T", "confidence": 1.7},
             "publisher": {"value": "P", "confidence": -0.4},
             "subtitle": {"value": "S"},
             "contributors": [{"name": "A", "role": "Author", "confidence": 5}]}
            """);

        Assert.Equal(1.0, reading!.Title!.Confidence);
        Assert.Equal(0.0, reading.Publisher!.Confidence);
        Assert.Equal(0.0, reading.Subtitle!.Confidence);
        Assert.Equal(1.0, reading.Contributors[0].Confidence);
    }

    [Theory]
    [InlineData("Illustrator", AuthorRole.Illustrator)]
    [InlineData("ILLUSTRATOR", AuthorRole.Illustrator)]
    [InlineData("Ghostwriter", AuthorRole.Contributor)]
    [InlineData("99", AuthorRole.Contributor)]
    [InlineData("", AuthorRole.Contributor)]
    public void Role_IsMatchedCaseInsensitivelyAndUnknownBecomesContributor(string role, AuthorRole expected)
    {
        var reading = SharedParsers.ParseBookPhotoReading(
            $$$"""{"contributors": [{"name": "A", "role": "{{{role}}}", "confidence": 0.5}]}""");

        Assert.Equal(expected, reading!.Contributors[0].Role);
    }

    [Fact]
    public void ReferenceHint_KindIsIgnoredWhenNotFlagged()
    {
        var reading = SharedParsers.ParseBookPhotoReading(
            """{"title": {"value": "T", "confidence": 1}, "reference_work": {"is_reference_work": false, "kind": "atlas", "confidence": 0.9}}""");

        Assert.Equal(new ReferenceWorkHint(false, null, 0.9), reading!.ReferenceWork);
    }
}
