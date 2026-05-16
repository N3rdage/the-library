using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedGenreExpansion : Migration
    {
        // Idempotent NOT-EXISTS guard per row, mirroring SeedHorrorSubGenres.
        private static readonly string[] SciFiSubGenres =
        [
            "Hard SF",
            "Space Opera",
            "Cyberpunk",
            "Military SF",
            "Time Travel",
            "First Contact",
            "Post-Apocalyptic",
            "Dystopian SF",
            "Alternate History",
            "Young Adult SF",
        ];

        private static readonly string[] HorrorSubGenres =
        [
            "Cosmic Horror",
            "Gothic Horror",
            "Supernatural / Ghost Story",
            "Psychological Horror",
            "Splatterpunk",
            "Folk Horror",
            "Body Horror",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedUnder(migrationBuilder, "Science Fiction", SciFiSubGenres);
            SeedUnder(migrationBuilder, "Horror", HorrorSubGenres);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in SciFiSubGenres)
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            foreach (var name in HorrorSubGenres)
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
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
    }
}
