using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Attaches a tag (by name) to a book, creating the tag if it doesn't exist via
// TagResolver (which owns name normalisation). Relocated from
// BookDetailViewModel.AddTagAsync in PR6b-3. Returns the resolved tag (id + its
// actual stored Name, which may differ in casing from the input when it matched
// an existing row), or null if the name is blank or the book is gone.
// Idempotent: re-adding a tag already on the book is a no-op that still returns
// the tag.
public sealed record AddTagToBook(int BookId, string TagName) : ICommand<TagDetail?>;

public sealed class AddTagToBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddTagToBook, TagDetail?>
{
    public async Task<TagDetail?> HandleAsync(AddTagToBook command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.TagName)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.Include(b => b.Tags).FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        if (book is null) return null;

        var tag = await TagResolver.FindOrCreateAsync(command.TagName, db, ct);

        // Attach only if not already present. tag.Id == 0 is a brand-new
        // (unsaved) tag, which by definition isn't on the book yet.
        if (tag.Id == 0 || book.Tags.All(t => t.Id != tag.Id))
            book.Tags.Add(tag);

        await db.SaveChangesAsync(ct);
        return new TagDetail(tag.Id, tag.Name);
    }
}
