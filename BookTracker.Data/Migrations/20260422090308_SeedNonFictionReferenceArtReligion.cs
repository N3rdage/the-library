using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedNonFictionReferenceArtReligion : Migration
    {
        // First non-fiction starter set — reference books (dictionaries,
        // atlases, field guides), art books (history, monographs, design),
        // and religious texts / study. Idempotent: each row guarded by
        // NOT EXISTS so re-running on an already-migrated DB is a no-op.
        // Pattern matches SeedHorrorSubGenres and AddGenreHierarchyAndSeed.

        private static readonly string[] TopLevel =
        [
            "Reference",
            "Art",
            "Religion & Spirituality",
        ];

        private static readonly (string Child, string Parent)[] SubGenres =
        [
            ("Dictionaries", "Reference"),
            ("Encyclopedias", "Reference"),
            ("Atlases", "Reference"),
            ("Field Guides", "Reference"),
            ("Style Guides", "Reference"),
            ("Language Learning", "Reference"),

            ("Art History", "Art"),
            ("Artist Monographs", "Art"),
            ("Art Theory", "Art"),
            ("Photography", "Art"),
            ("Architecture", "Art"),
            ("Design", "Art"),

            ("Sacred Texts", "Religion & Spirituality"),
            ("Biblical Studies", "Religion & Spirituality"),
            ("Theology", "Religion & Spirituality"),
            ("Comparative Religion", "Religion & Spirituality"),
            ("Mythology", "Religion & Spirituality"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in TopLevel)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{Escape(name)}' AND [ParentGenreId] IS NULL)
    INSERT INTO [Genres] ([Name], [ParentGenreId]) VALUES (N'{Escape(name)}', NULL);");
            }

            foreach (var (child, parent) in SubGenres)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{Escape(child)}')
    INSERT INTO [Genres] ([Name], [ParentGenreId])
    VALUES (N'{Escape(child)}',
            (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'{Escape(parent)}' AND [ParentGenreId] IS NULL));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Children first (FK references), then parents. Doesn't touch
            // BookGenre links — if any books got tagged with these new
            // genres, those links disappear when the row goes; that's a
            // deliberate "revert means reverse" stance. In practice
            // rolling back a seed migration is rare.
            foreach (var (child, _) in SubGenres)
            {
                migrationBuilder.Sql($@"DELETE FROM [BookGenre] WHERE [GenresId] IN (SELECT [Id] FROM [Genres] WHERE [Name] = N'{Escape(child)}');");
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{Escape(child)}';");
            }

            foreach (var name in TopLevel)
            {
                migrationBuilder.Sql($@"DELETE FROM [BookGenre] WHERE [GenresId] IN (SELECT [Id] FROM [Genres] WHERE [Name] = N'{Escape(name)}' AND [ParentGenreId] IS NULL);");
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{Escape(name)}' AND [ParentGenreId] IS NULL;");
            }
        }

        private static string Escape(string s) => s.Replace("'", "''");
    }
}
