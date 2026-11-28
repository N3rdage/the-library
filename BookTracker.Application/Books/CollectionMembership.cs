using System.Linq.Expressions;
using BookTracker.Data.Models;

namespace BookTracker.Application.Books;

// The one rule for "is this book in the collection": it is while it has at
// least one owned copy, and a book with no copies at all stays in. A book is
// removed exactly when it has copies and none of them is owned. Every read that
// treats the library as "what I own" filters through these expressions so the
// rule is written once. They translate to SQL EXISTS subqueries.
public static class CollectionMembership
{
    public static readonly Expression<Func<Book, bool>> IsRemoved = b =>
        b.Editions.Any(e => e.Copies.Any())
        && !b.Editions.Any(e => e.Copies.Any(c => c.Disposition == CopyDisposition.Owned));

    public static readonly Expression<Func<Book, bool>> IsInCollection = b =>
        !b.Editions.Any(e => e.Copies.Any())
        || b.Editions.Any(e => e.Copies.Any(c => c.Disposition == CopyDisposition.Owned));

    public static IQueryable<Book> InCollection(this IQueryable<Book> books) => books.Where(IsInCollection);

    public static IQueryable<Book> Removed(this IQueryable<Book> books) => books.Where(IsRemoved);
}
