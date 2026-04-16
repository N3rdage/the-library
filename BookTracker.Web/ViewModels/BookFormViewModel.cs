using System.ComponentModel.DataAnnotations;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

public class BookFormViewModel
{
    public static string FormatCategory(BookCategory c) => c switch
    {
        BookCategory.NonFiction => "Non-Fiction",
        _ => c.ToString()
    };

    public class BookFormInput
    {
        [Required, StringLength(300)]
        public string? Title { get; set; }

        [StringLength(300)]
        public string? Subtitle { get; set; }

        [Required, StringLength(200)]
        public string? Author { get; set; }

        public BookCategory Category { get; set; } = BookCategory.Fiction;

        public BookStatus Status { get; set; } = BookStatus.Read;

        [Range(0, 5)]
        public int Rating { get; set; }

        public string? Notes { get; set; }

        [StringLength(500)]
        public string? DefaultCoverArtUrl { get; set; }
    }
}
