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

        public DateOnly? FirstPublishedDate { get; set; }
    }
}
