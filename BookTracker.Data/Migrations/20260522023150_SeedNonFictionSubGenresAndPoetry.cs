using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedNonFictionSubGenresAndPoetry : Migration
    {
        // Idempotent NOT-EXISTS guard per row, mirroring SeedGenreExpansion.
        // Round 2 (2026-05-22) of the genre-restructure arc — surfaces three
        // missing non-fiction branches (popular science, memoir, philosophy)
        // and one missing top-level fiction-adjacent branch (poetry) that the
        // bulk-changeset review identified as currently misclassified under
        // Reference and Literary Fiction respectively.
        private static readonly string[] ReferenceSubGenres =
        [
            "Popular Science",
            "Memoir",
            "Philosophy",
        ];

        private static readonly string[] NewTopLevel =
        [
            "Poetry",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedUnder(migrationBuilder, "Reference", ReferenceSubGenres);
            SeedTopLevel(migrationBuilder, NewTopLevel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in ReferenceSubGenres)
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in NewTopLevel)
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
