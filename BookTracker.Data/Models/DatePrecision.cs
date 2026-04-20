namespace BookTracker.Data.Models;

// Companion column for date fields where the user often only knows part
// of the date — print dates on older books frequently come as "1973" or
// "October 1973" without a day. The DateOnly column still stores a real
// date (with month/day defaulted to 1) so sorting works; the precision
// flag drives display ("1973" vs "Oct 1973" vs "12 Oct 1973").
public enum DatePrecision
{
    Day = 0,
    Month = 1,
    Year = 2,
}
