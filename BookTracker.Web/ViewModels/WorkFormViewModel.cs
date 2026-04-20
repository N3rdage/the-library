using System.ComponentModel.DataAnnotations;

namespace BookTracker.Web.ViewModels;

// Per-Work editor data. Used standalone on the Add Book page (single Work
// is auto-created with the Book) and inline on the Edit Book page where
// each Work is editable as its own card.
public class WorkFormViewModel
{
    public class WorkFormInput
    {
        [Required, StringLength(300)]
        public string? Title { get; set; }

        [StringLength(300)]
        public string? Subtitle { get; set; }

        [Required, StringLength(200)]
        public string? Author { get; set; }

        // Free-form text — accepts "1973", "Oct 1973", "12 Oct 1973",
        // "1973-10", "1973-10-12". Parsed into Work.FirstPublishedDate +
        // Work.FirstPublishedDatePrecision at save time.
        public string? FirstPublishedDate { get; set; }
    }
}
