using System.ComponentModel.DataAnnotations;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class EditionFormViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
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

    public record PublisherOption(int Id, string Name);

    public class EditionFormInput
    {
        [Required, StringLength(20)]
        [RegularExpression(@"^(97(8|9))?\d{9}(\d|X|x)$", ErrorMessage = "Enter a valid 10- or 13-digit ISBN.")]
        public string? Isbn { get; set; }

        public BookFormat Format { get; set; } = BookFormat.TradePaperback;

        public DateOnly? DatePrinted { get; set; }

        [StringLength(200)]
        public string? Publisher { get; set; }

        [StringLength(500)]
        public string? CoverUrl { get; set; }
    }
}
