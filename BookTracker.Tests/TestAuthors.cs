using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for pen-name rows: Author's CanonicalAuthorId setter is private,
// so tests make an alias the way the app does, through Author.MarkAliasOf.
// Works for a canonical that is saved (key set) or still unsaved in the same
// context (the navigation links them on insert).
public static class TestAuthors
{
    public static Author Alias(string name, Author of)
    {
        var alias = Author.Create(name);
        alias.MarkAliasOf(of);
        return alias;
    }
}
