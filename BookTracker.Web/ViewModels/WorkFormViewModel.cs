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

        // Multi-author input — populated by MudAuthorPicker as the user adds
        // chips. Save path requires at least one entry; uses the list to
        // dual-write Work.Author (legacy lead-author FK) and Work.WorkAuthors
        // (the new M:N join with Order). DataAnnotations [Required] doesn'\''t
        // map cleanly to "non-empty list of non-empty strings", so this is
        // validated at save time rather than via the validator.
        public List<string> Authors { get; set; } = [];

        // Free-form text — accepts "1973", "Oct 1973", "12 Oct 1973",
        // "1973-10", "1973-10-12". Parsed into Work.FirstPublishedDate +
        // Work.FirstPublishedDatePrecision at save time.
        public string? FirstPublishedDate { get; set; }
    }
}
