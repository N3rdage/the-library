using System.Text.RegularExpressions;
using BookTracker.Application;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SeriesEdit = BookTracker.Web.Components.Pages.Series.Edit;

namespace BookTracker.Tests.Components;

[Trait("Category", TestCategories.Component)]
public class HelpDialogTests : ComponentTestBase
{
    public static TheoryData<HelpTopic, string> Topics => new()
    {
        { HelpTopic.SeriesOrder, "manual#series" },
        { HelpTopic.AddBook, "manual#add-book" },
        { HelpTopic.BulkAdd, "manual#bulk-add" },
    };

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(HelpTopic topic)
    {
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<HelpDialog> { { x => x.Topic, topic } };
        await dialogs.InvokeAsync(() => dialogService.ShowAsync<HelpDialog>(HelpDialog.TitleFor(topic), parameters));
        return dialogs;
    }

    [Theory]
    [MemberData(nameof(Topics))]
    public async Task Topic_showsItsTitle_andTheManualLink(HelpTopic topic, string href)
    {
        var dialogs = await OpenAsync(topic);

        dialogs.WaitForAssertion(() => Assert.Contains(HelpDialog.TitleFor(topic), dialogs.Markup));
        Assert.Equal(href, dialogs.Find("a[href^='manual#']").GetAttribute("href"));
        Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Close");
    }

    [Fact]
    public void ManualLinks_resolveToHeadingsInTheShippedGuide()
    {
        var doc = new MarkdownDocumentService([AppContext.BaseDirectory]).Get("user-guide");
        Assert.NotNull(doc);
        var ids = Regex.Matches(doc.Html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        foreach (var topic in Enum.GetValues<HelpTopic>())
        {
            var href = HelpDialog.ManualHref(topic);
            Assert.StartsWith("manual#", href);
            var id = href["manual#".Length..];
            Assert.True(ids.Contains(id), $"USER-GUIDE.md has no heading with id '{id}'");
        }
    }

    [Fact]
    public void SeriesEditPage_hasAHelpButton()
    {
        Services.AddSingleton(Substitute.For<IDispatcher>());
        Services.AddTransient<SeriesEditViewModel>();
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();

        var cut = Render<SeriesEdit>();

        Assert.Single(cut.FindAll("button[aria-label='Help']"));
    }
}
