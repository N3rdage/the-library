using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedHorrorSubGenres : Migration
    {
        // Idempotent: each row is guarded by NOT EXISTS so re-running the
        // migration on a DB where the row already exists is a no-op.
        // Mirrors the pattern from AddGenreHierarchyAndSeed.
        private static readonly string[] SubGenres = ["Cthulhu Mythos", "Vampire", "Zombie"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in SubGenres)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{name}')
    INSERT INTO [Genres] ([Name], [ParentGenreId])
    VALUES (N'{name}',
            (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'Horror' AND [ParentGenreId] IS NULL));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in SubGenres)
            {
                migrationBuilder.Sql($@"DELETE FROM [Genres] WHERE [Name] = N'{name}';");
            }
        }
    }
}
