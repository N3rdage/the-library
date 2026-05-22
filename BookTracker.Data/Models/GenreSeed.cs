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
        new("Hard SF", "Science Fiction"),
        new("Space Opera", "Science Fiction"),
        new("Cyberpunk", "Science Fiction"),
        new("Military SF", "Science Fiction"),
        new("Time Travel", "Science Fiction"),
        new("First Contact", "Science Fiction"),
        new("Post-Apocalyptic", "Science Fiction"),
        new("Dystopian SF", "Science Fiction"),
        new("Alternate History", "Science Fiction"),
        new("Young Adult SF", "Science Fiction"),

        new("Historical Fiction", null),
        new("Horror", null),
        new("Cthulhu Mythos", "Horror"),
        new("Vampire", "Horror"),
        new("Zombie", "Horror"),
        new("Cosmic Horror", "Horror"),
        new("Gothic Horror", "Horror"),
        new("Supernatural / Ghost Story", "Horror"),
        new("Psychological Horror", "Horror"),
        new("Splatterpunk", "Horror"),
        new("Folk Horror", "Horror"),
        new("Body Horror", "Horror"),
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
        // `Graphic Novels` and `Short Story Collections` were removed
        // 2026-05-17 — they're format indicators, not genres. They now
        // live as `format:graphic-novel` / `format:short-stories` tags
        // on the Book. See GENRE-TAXONOMY.md Provenance for the
        // RemoveFormatGenres migration and DATA-DICTIONARY.md §Tag.


        // Non-fiction. Reference/Art/Religion were the April starter set; the
        // 2026-05-22 round-2 work added Popular Science, Memoir, Philosophy as
        // Reference children and Poetry as a top-level. Round 3 (2026-05-23)
        // re-parented Memoir under Biography and Popular Science under Science
        // now that those parents exist, and added the rest of the non-fiction
        // tree (History, Biography, Science, Psychology & Self-help, Travel
        // Writing, Politics & Current Affairs, Media Studies) plus Performing
        // Arts as a fiction-side branch for plays/screenplays. See Provenance
        // in GENRE-TAXONOMY.md for the migration trail.
        new("Reference", null),
        new("Dictionaries", "Reference"),
        new("Encyclopedias", "Reference"),
        new("Atlases", "Reference"),
        new("Field Guides", "Reference"),
        new("Style Guides", "Reference"),
        new("Language Learning", "Reference"),
        new("Philosophy", "Reference"),
        new("Cookery", "Reference"),
        new("Travel Guides", "Reference"),
        new("How-to & Instruction", "Reference"),

        new("Poetry", null),

        new("Art", null),
        new("Art History", "Art"),
        new("Artist Monographs", "Art"),
        new("Art Theory", "Art"),
        new("Photography", "Art"),
        new("Architecture", "Art"),
        new("Design", "Art"),
        new("Music", "Art"),

        new("Religion & Spirituality", null),
        new("Sacred Texts", "Religion & Spirituality"),
        new("Biblical Studies", "Religion & Spirituality"),
        new("Theology", "Religion & Spirituality"),
        new("Comparative Religion", "Religion & Spirituality"),
        new("Mythology", "Religion & Spirituality"),

        new("History", null),
        new("Ancient History", "History"),
        new("Medieval History", "History"),
        new("Modern History", "History"),
        new("Military History", "History"),
        new("Local & Regional History", "History"),
        new("Social & Cultural History", "History"),
        new("Popular History", "History"),

        new("Biography", null),
        new("Memoir", "Biography"),
        new("Autobiography", "Biography"),
        new("Authorised Biography", "Biography"),
        new("Unauthorised Biography", "Biography"),
        new("Letters & Diaries", "Biography"),

        new("Science", null),
        new("Popular Science", "Science"),
        new("Mathematics", "Science"),
        new("Physics & Astronomy", "Science"),
        new("Biology & Natural History", "Science"),
        new("Earth & Environmental Science", "Science"),
        new("Medicine & Anatomy", "Science"),
        new("Computer Science", "Science"),

        new("Psychology & Self-help", null),
        new("Cognitive Science", "Psychology & Self-help"),
        new("Clinical & Therapeutic", "Psychology & Self-help"),
        new("Social Psychology", "Psychology & Self-help"),
        new("Self-help & Productivity", "Psychology & Self-help"),
        new("Philosophy of Mind", "Psychology & Self-help"),

        new("Travel Writing", null),
        new("Politics & Current Affairs", null),
        new("Media Studies", null),

        new("Performing Arts", null),
        new("Stage Plays", "Performing Arts"),
        new("Screenplays & TV Scripts", "Performing Arts"),
    };
}
