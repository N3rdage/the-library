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

        // Non-Author contributors (editor / translator / illustrator / etc.)
        // — populated by MudContributorPicker. Optional; empty for the
        // common single-Author case. Save path appends one WorkAuthor row
        // per entry alongside the Author rows resolved from Authors.
        public List<ContributorEntry> Contributors { get; set; } = [];

        // Free-form text — accepts "1973", "Oct 1973", "12 Oct 1973",
        // "1973-10", "1973-10-12". Parsed into Work.FirstPublishedDate +
        // Work.FirstPublishedDatePrecision at save time.
        public string? FirstPublishedDate { get; set; }

        // Per-Work genre selection — used by the collection-mode "Single-Genre
        // off" path so each row can carry its own genre chips. Single-Work
        // captures still route genre selection through the page's own
        // selectedGenreIds field rather than this list.
        public List<int> GenreIds { get; set; } = [];

        // Attach-existing mode (collection rows only). When AttachedWorkId is
        // set the row represents "attach this already-saved Work to the new
        // Book" rather than "create a new Work with the fields above" —
        // save-time skips the create branch for that row, validation skips
        // the title/author requirement, and the UI hides the editable fields
        // in favour of a compact summary card. AttachedWorkAuthor is the
        // lead-author display string captured at pick time so the summary
        // can render without re-querying.
        public int? AttachedWorkId { get; set; }
        public string? AttachedWorkAuthor { get; set; }
    }
}
