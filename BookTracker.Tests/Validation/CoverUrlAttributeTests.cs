using System.ComponentModel.DataAnnotations;
using BookTracker.Web.ViewModels;
using BookTracker.Web.Validation;

namespace BookTracker.Tests.Validation;

[Trait("Category", TestCategories.Unit)]
public class CoverUrlAttributeTests
{
    private sealed class Model
    {
        [CoverUrl]
        public string? CoverUrl { get; set; }
    }

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://covers.openlibrary.org/b/isbn/x-L.jpg")]
    public void AcceptableValue_HasNoErrors(string? url)
    {
        Assert.Empty(Validate(new Model { CoverUrl = url }));
    }

    [Theory]
    [InlineData("http://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("https://10.0.0.5/x.jpg")]
    [InlineData("https://user:pw@covers.openlibrary.org/x.jpg")]
    [InlineData("https://covers.openlibrary.org:8443/x.jpg")]
    public void UnacceptableValue_ReportsMessageOnTheProperty(string url)
    {
        var error = Assert.Single(Validate(new Model { CoverUrl = url }));

        Assert.Equal(CoverUrlAttribute.Message, error.ErrorMessage);
        Assert.Equal(nameof(Model.CoverUrl), Assert.Single(error.MemberNames));
    }

    [Fact]
    public void FormInputs_CarryTheAttribute()
    {
        var book = new BookFormViewModel.BookFormInput { Title = "Mort", DefaultCoverArtUrl = "http://covers.example.org/x.jpg" };
        var edition = new EditionFormViewModel.EditionFormInput { CoverUrl = "http://covers.example.org/x.jpg" };

        Assert.Contains(Validate(book), r => r.MemberNames.Contains(nameof(book.DefaultCoverArtUrl)));
        Assert.Contains(Validate(edition), r => r.MemberNames.Contains(nameof(edition.CoverUrl)));
    }
}
