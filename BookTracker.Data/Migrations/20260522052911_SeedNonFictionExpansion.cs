using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// PR 1 of the 2026-05-23 non-fiction expansion. Seeds the full non-fiction
    /// tree (History / Biography / Science / Psychology &amp; Self-help / Travel
    /// Writing / Politics &amp; Current Affairs / Media Studies) plus Performing
    /// Arts as a fiction-side branch, plus four extensions to existing branches
    /// (Cookery / Travel Guides / How-to &amp; Instruction under Reference; Music
    /// under Art), plus five new format:* tags.
    ///
    /// Also re-parents Memoir and Popular Science (Round-2 placed them under
    /// Reference as a pragmatic stopgap; now that Biography and Science exist,
    /// they move under their taxonomically-correct parents in place — no
    /// GenreWork churn). A follow-up .debug/data-fixes/.ps1 swaps the Reference
    /// tag for Biography / Science on the 6 affected Works post-deploy.
    ///
    /// Down() will fail if any Work has been tagged with the new Genres
    /// (FK from GenreWork.GenreId) — manual cleanup required before rollback.
    /// </remarks>
    public partial class SeedNonFictionExpansion : Migration
    {
        // New top-level branches and their sub-genres. Mirrors the
        // SeedGenreExpansion / SeedNonFictionSubGenresAndPoetry pattern.
        private static readonly string[] HistorySubGenres =
        [
            "Ancient History",
            "Medieval History",
            "Modern History",
            "Military History",
            "Local & Regional History",
            "Social & Cultural History",
            "Popular History",
        ];

        // Memoir is NOT in this list — it already exists from
        // SeedNonFictionSubGenresAndPoetry (2026-05-22) and is re-parented
        // below rather than re-inserted.
        private static readonly string[] BiographySubGenresExceptMemoir =
        [
            "Autobiography",
            "Authorised Biography",
            "Unauthorised Biography",
            "Letters & Diaries",
        ];

        // Popular Science is NOT in this list — same reason as Memoir.
        private static readonly string[] ScienceSubGenresExceptPopSci =
        [
            "Mathematics",
            "Physics & Astronomy",
            "Biology & Natural History",
            "Earth & Environmental Science",
            "Medicine & Anatomy",
            "Computer Science",
        ];

        private static readonly string[] PsychologySubGenres =
        [
            "Cognitive Science",
            "Clinical & Therapeutic",
            "Social Psychology",
            "Self-help & Productivity",
            "Philosophy of Mind",
        ];

        private static readonly string[] PerformingArtsSubGenres =
        [
            "Stage Plays",
            "Screenplays & TV Scripts",
        ];

        private static readonly string[] NewTopLevelRoots =
        [
            "History",
            "Biography",
            "Science",
            "Psychology & Self-help",
            "Performing Arts",
            "Travel Writing",
            "Politics & Current Affairs",
            "Media Studies",
        ];

        private static readonly string[] ReferenceExtensions =
        [
            "Cookery",
            "Travel Guides",
            "How-to & Instruction",
        ];

        private static readonly string[] ArtExtensions =
        [
            "Music",
        ];

        private static readonly string[] FormatTags =
        [
            "format:reference",
            "format:notebook",
            "format:script",
            "format:textbook",
            "format:illustrated",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New top-level roots (all 8 in one helper call).
            SeedTopLevel(migrationBuilder, NewTopLevelRoots);

            // Sub-genres for the new roots.
            SeedUnder(migrationBuilder, "History",                HistorySubGenres);
            SeedUnder(migrationBuilder, "Biography",              BiographySubGenresExceptMemoir);
            SeedUnder(migrationBuilder, "Science",                ScienceSubGenresExceptPopSci);
            SeedUnder(migrationBuilder, "Psychology & Self-help", PsychologySubGenres);
            SeedUnder(migrationBuilder, "Performing Arts",        PerformingArtsSubGenres);

            // Extensions to existing branches.
            SeedUnder(migrationBuilder, "Reference", ReferenceExtensions);
            SeedUnder(migrationBuilder, "Art",       ArtExtensions);

            // Re-parent Memoir + Popular Science from Reference to their
            // taxonomically-correct new parents. In-place update keeps the
            // existing Genre IDs so GenreWork rows don't need touching.
            migrationBuilder.Sql(@"
UPDATE [Genres]
SET    [ParentGenreId] = (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'Biography' AND [ParentGenreId] IS NULL)
WHERE  [Name] = N'Memoir';");

            migrationBuilder.Sql(@"
UPDATE [Genres]
SET    [ParentGenreId] = (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'Science' AND [ParentGenreId] IS NULL)
WHERE  [Name] = N'Popular Science';");

            // Format tags. Idempotent by Name.
            foreach (var tagName in FormatTags)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Tags] WHERE [Name] = N'{tagName}')
    INSERT INTO [Tags] ([Name]) VALUES (N'{tagName}');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tags first.
            foreach (var tagName in FormatTags)
                migrationBuilder.Sql($@"DELETE FROM [Tags] WHERE [Name] = N'{tagName}';");

            // Reverse the re-parent.
            migrationBuilder.Sql(@"
UPDATE [Genres]
SET    [ParentGenreId] = (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'Reference' AND [ParentGenreId] IS NULL)
WHERE  [Name] = N'Memoir';");

            migrationBuilder.Sql(@"
UPDATE [Genres]
SET    [ParentGenreId] = (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'Reference' AND [ParentGenreId] IS NULL)
WHERE  [Name] = N'Popular Science';");

            // Delete extensions to existing branches.
            foreach (var name in ArtExtensions)       migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in ReferenceExtensions) migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");

            // Delete new sub-genres (children of new roots).
            foreach (var name in PerformingArtsSubGenres)        migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in PsychologySubGenres)            migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in ScienceSubGenresExceptPopSci)   migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in BiographySubGenresExceptMemoir) migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in HistorySubGenres)               migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");

            // Delete new top-level roots.
            foreach (var name in NewTopLevelRoots)
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}' AND [ParentGenreId] IS NULL;");
        }

        private static void SeedUnder(MigrationBuilder migrationBuilder, string parentName, string[] names)
        {
            foreach (var name in names)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{name}')
    INSERT INTO [Genres] ([Name], [ParentGenreId])
    VALUES (N'{name}',
            (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'{parentName}' AND [ParentGenreId] IS NULL));");
            }
        }

        private static void SeedTopLevel(MigrationBuilder migrationBuilder, string[] names)
        {
            foreach (var name in names)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{name}' AND [ParentGenreId] IS NULL)
    INSERT INTO [Genres] ([Name], [ParentGenreId])
    VALUES (N'{name}', NULL);");
            }
        }
    }
}
