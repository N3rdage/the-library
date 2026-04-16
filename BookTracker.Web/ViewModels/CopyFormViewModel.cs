using System.ComponentModel.DataAnnotations;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class CopyFormViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public List<PublisherOption> ExistingPublishers { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        ExistingPublishers = await db.Publishers
            .OrderBy(p => p.Name)
            .Select(p => new PublisherOption(p.Id, p.Name))
            .ToListAsync();
    }

    public static string FormatCondition(BookCondition c) => c switch
    {
        BookCondition.AsNew => "As New",
        BookCondition.VeryGood => "Very Good",
        _ => c.ToString()
    };

    public record PublisherOption(int Id, string Name);

    public class CopyFormInput
    {
        [Required, StringLength(20)]
        [RegularExpression(@"^(97(8|9))?\d{9}(\d|X|x)$", ErrorMessage = "Enter a valid 10- or 13-digit ISBN.")]
        public string? Isbn { get; set; }

        public BookFormat Format { get; set; } = BookFormat.Softcopy;

        public DateOnly? DatePrinted { get; set; }

        public BookCondition Condition { get; set; } = BookCondition.Good;

        [StringLength(200)]
        public string? Publisher { get; set; }

        [StringLength(500)]
        public string? CustomCoverArtUrl { get; set; }
    }
}
