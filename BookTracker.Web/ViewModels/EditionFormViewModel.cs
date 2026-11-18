using System.ComponentModel.DataAnnotations;
using BookTracker.Application;
using BookTracker.Application.Publishers;
using BookTracker.Data.Models;
using BookTracker.Web.Validation;

namespace BookTracker.Web.ViewModels;

public class EditionFormViewModel(IDispatcher dispatcher)
{
    // A mutable copy: a publisher created from the form is appended to it.
    public List<PublisherOption> ExistingPublishers { get; private set; } = [];

    public async Task InitializeAsync()
    {
        ExistingPublishers = [.. await dispatcher.Query(new GetPublisherOptions())];
    }

    public class EditionFormInput
    {
        // Optional to allow pre-1974 books with no ISBN. RegularExpression
        // is skipped for null/empty values by default, so the regex only
        // fires when something is entered.
        [StringLength(20)]
        [RegularExpression(@"^(97(8|9))?\d{9}(\d|X|x)$", ErrorMessage = "Enter a valid 10- or 13-digit ISBN.")]
        public string? Isbn { get; set; }

        public BookFormat Format { get; set; } = BookFormat.TradePaperback;

        // Revision number — 1st ed., 2nd ed., etc. Nullable because most
        // editions don't surface this. Range guard: positive integer.
        [Range(1, 99, ErrorMessage = "Edition number must be 1 or higher.")]
        public int? EditionNumber { get; set; }

        // Free-form text — accepts "1973", "Oct 1973", "12 Oct 1973",
        // "1973-10", "1973-10-12". Parsed into Edition.DatePrinted +
        // Edition.DatePrintedPrecision at save time.
        public string? DatePrinted { get; set; }

        [StringLength(200)]
        public string? Publisher { get; set; }

        [StringLength(500)]
        [CoverUrl]
        public string? CoverUrl { get; set; }
    }
}
