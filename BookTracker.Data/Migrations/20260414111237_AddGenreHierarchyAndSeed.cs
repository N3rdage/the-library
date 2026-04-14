using System.Linq;
using System.Text;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGenreHierarchyAndSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentGenreId",
                table: "Genres",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_ParentGenreId",
                table: "Genres",
                column: "ParentGenreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Genres_Genres_ParentGenreId",
                table: "Genres",
                column: "ParentGenreId",
                principalTable: "Genres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BuildSeedAndCleanupSql());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort revert: clear the parent FK column, drop the FK + index +
            // column. We don't try to restore deleted free-text genres — they're
            // unrecoverable by design (this migration is the cutover point).
            migrationBuilder.Sql("UPDATE [Genres] SET [ParentGenreId] = NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Genres_Genres_ParentGenreId",
                table: "Genres");

            migrationBuilder.DropIndex(
                name: "IX_Genres_ParentGenreId",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "ParentGenreId",
                table: "Genres");
        }

        private static string BuildSeedAndCleanupSql()
        {
            var sb = new StringBuilder();

            // Insert top-level genres if missing (parent NULL).
            foreach (var entry in GenreSeed.All.Where(e => e.ParentName is null))
            {
                sb.AppendLine($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{Escape(entry.Name)}' AND [ParentGenreId] IS NULL)
    INSERT INTO [Genres] ([Name], [ParentGenreId]) VALUES (N'{Escape(entry.Name)}', NULL);");
            }

            // Insert sub-genres if missing, parent looked up by name.
            foreach (var entry in GenreSeed.All.Where(e => e.ParentName is not null))
            {
                sb.AppendLine($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{Escape(entry.Name)}')
    INSERT INTO [Genres] ([Name], [ParentGenreId])
    VALUES (N'{Escape(entry.Name)}',
            (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'{Escape(entry.ParentName!)}' AND [ParentGenreId] IS NULL));");
            }

            // For pre-existing rows whose names match a sub-genre but lack parent,
            // attach the parent. Idempotent.
            foreach (var entry in GenreSeed.All.Where(e => e.ParentName is not null))
            {
                sb.AppendLine($@"
UPDATE [Genres]
SET [ParentGenreId] = (SELECT TOP 1 [Id] FROM [Genres] WHERE [Name] = N'{Escape(entry.ParentName!)}' AND [ParentGenreId] IS NULL)
WHERE [Name] = N'{Escape(entry.Name)}' AND [ParentGenreId] IS NULL;");
            }

            // Cleanup: drop book-genre links for genres not in the preset list,
            // then drop the orphan genre rows themselves.
            var nameList = string.Join(", ", GenreSeed.All.Select(e => $"N'{Escape(e.Name)}'"));
            sb.AppendLine($@"
DELETE bg FROM [BookGenre] bg
INNER JOIN [Genres] g ON bg.[GenresId] = g.[Id]
WHERE g.[Name] NOT IN ({nameList});

DELETE FROM [Genres] WHERE [Name] NOT IN ({nameList});");

            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("'", "''");
    }
}
