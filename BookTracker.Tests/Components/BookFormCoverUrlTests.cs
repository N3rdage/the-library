using BookTracker.Web.Components.Shared;
using BookTracker.Web.Validation;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for the cover URL field on BookForm: inside an EditForm with a
/// DataAnnotationsValidator (the Add page's shape), a URL the cover mirror would
/// refuse shows the field message and the form does not submit.
/// </summary>
[Trait("Category", TestCategories.Component)]
public class BookFormCoverUrlTests : ComponentTestBase
{
    private IRenderedComponent<EditForm> RenderInForm(BookFormViewModel.BookFormInput input, Action onValidSubmit)
        => Render<EditForm>(p => p
            .Add(f => f.Model, input)
            .Add(f => f.OnValidSubmit, onValidSubmit)
            .Add(f => f.ChildContent, (EditContext _) => (RenderFragment)(b =>
            {
                b.OpenComponent<DataAnnotationsValidator>(0);
                b.CloseComponent();
                b.OpenComponent<BookForm>(1);
                b.AddAttribute(2, nameof(BookForm.Input), input);
                b.CloseComponent();
            })));

    [Fact]
    public async Task HttpCoverUrl_ShowsMessageAndDoesNotSubmit()
    {
        var input = new BookFormViewModel.BookFormInput { Title = "Mort", DefaultCoverArtUrl = "http://covers.example.org/x.jpg" };
        var submitted = false;
        var cut = RenderInForm(input, () => submitted = true);

        await cut.Find("form").SubmitAsync();

        Assert.False(submitted);
        Assert.Contains(CoverUrlAttribute.Message, cut.Markup);
    }

    [Fact]
    public async Task HttpsCoverUrl_Submits()
    {
        var input = new BookFormViewModel.BookFormInput { Title = "Mort", DefaultCoverArtUrl = "https://covers.example.org/x.jpg" };
        var submitted = false;
        var cut = RenderInForm(input, () => submitted = true);

        await cut.Find("form").SubmitAsync();

        Assert.True(submitted);
        Assert.DoesNotContain(CoverUrlAttribute.Message, cut.Markup);
    }
}
