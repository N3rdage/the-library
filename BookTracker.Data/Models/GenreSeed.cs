namespace BookTracker.Data.Models;

// Source-of-truth taxonomy for the curated genre list. The migration uses this
// to seed/clean the Genres table; the Add page uses it implicitly via what's
// loaded from the DB. To extend the list, add an entry here and either ship a
// new migration or wait for the next migration to pick it up.
//
// Reference: https://fictionary.co/journal/book-genres/
public static class GenreSeed
{
    public record Entry(string Name, string? ParentName);

    public static readonly IReadOnlyList<Entry> All = new Entry[]
    {
        new("Fantasy", null),
        new("High (Epic) Fantasy", "Fantasy"),
        new("Urban (Contemporary) Fantasy", "Fantasy"),
        new("Dark Fantasy", "Fantasy"),
        new("Grimdark", "Fantasy"),
        new("Sword and Sorcery", "Fantasy"),
        new("Portal Fantasy", "Fantasy"),
        new("Fairy-Tale Retelling", "Fantasy"),
        new("Mythic Fantasy", "Fantasy"),
        new("Steampunk Fantasy", "Fantasy"),
        new("Paranormal Fantasy", "Fantasy"),
        new("Historical Fantasy", "Fantasy"),
        new("Young Adult Fantasy", "Fantasy"),

        new("Romance", null),
        new("Contemporary Romance", "Romance"),
        new("Historical Romance", "Romance"),
        new("Paranormal Romance", "Romance"),
        new("Romantic Suspense", "Romance"),
        new("Regency Romance", "Romance"),
        new("Dark Romance", "Romance"),
        new("Last Chance Romance", "Romance"),
        new("Enemies to Lovers", "Romance"),
        new("Fake Dating", "Romance"),
        new("Young Adult Romance", "Romance"),
        new("Romantasy", "Romance"),

        new("Mystery", null),
        new("Cozy Mystery", "Mystery"),
        new("Hard-Boiled / Detective", "Mystery"),
        new("Police Procedural", "Mystery"),
        new("Noir", "Mystery"),
        new("Historical Mystery", "Mystery"),
        new("Heist Mystery", "Mystery"),
        new("Whodunit", "Mystery"),
        new("Legal Thriller", "Mystery"),
        new("Private Detective", "Mystery"),

        new("Science Fiction", null),
        new("Historical Fiction", null),
        new("Horror", null),
        new("Cthulhu Mythos", "Horror"),
        new("Vampire", "Horror"),
        new("Zombie", "Horror"),
        new("Thriller", null),
        new("Adventure", null),
        new("Literary Fiction", null),
        new("Coming-of-Age", null),
        new("Satire", null),
        new("Dystopian", null),
        new("Utopian", null),
        new("Magical Realism", null),
        new("Biographical Fiction", null),
        new("Western", null),
        new("Graphic Novels", null),
        new("Short Story Collections", null),

        // Non-fiction starter set — reference, art, and religious books.
        // Further non-fiction branches (History, Biography, Science,
        // Poetry, etc.) are left for a follow-up expansion.
        new("Reference", null),
        new("Dictionaries", "Reference"),
        new("Encyclopedias", "Reference"),
        new("Atlases", "Reference"),
        new("Field Guides", "Reference"),
        new("Style Guides", "Reference"),
        new("Language Learning", "Reference"),

        new("Art", null),
        new("Art History", "Art"),
        new("Artist Monographs", "Art"),
        new("Art Theory", "Art"),
        new("Photography", "Art"),
        new("Architecture", "Art"),
        new("Design", "Art"),

        new("Religion & Spirituality", null),
        new("Sacred Texts", "Religion & Spirituality"),
        new("Biblical Studies", "Religion & Spirituality"),
        new("Theology", "Religion & Spirituality"),
        new("Comparative Religion", "Religion & Spirituality"),
        new("Mythology", "Religion & Spirituality"),
    };
}
