using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

public class CopyFormViewModel
{
    public static string FormatCondition(BookCondition c) => c switch
    {
        BookCondition.AsNew => "As New",
        BookCondition.VeryGood => "Very Good",
        _ => c.ToString()
    };

    public class CopyFormInput
    {
        public BookCondition Condition { get; set; } = BookCondition.Good;

        public DateTime? DateAcquired { get; set; }

        public string? Notes { get; set; }
    }
}
