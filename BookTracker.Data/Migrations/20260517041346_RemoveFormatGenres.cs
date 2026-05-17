using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFormatGenres : Migration
    {
        // `Graphic Novels` and `Short Story Collections` are format indicators,
        // not genres, and now live as `format:*` Tags on the Book. The DELETE
        // is guarded by "no GenreWork refs" so it's safe on a prod DB where the
        // once-off data-fix script hasn't run yet — the orphan rows simply
        // linger until cleanup, then drop on a later deploy. On a fresh install
        // (or any DB where the rows have no refs), the DELETE fires immediately.
        private static readonly string[] FormatGenres = ["Graphic Novels", "Short Story Collections"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Tags",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 2, "format:graphic-novel" },
                    { 3, "format:short-stories" }
                });

            foreach (var name in FormatGenres)
            {
                migrationBuilder.Sql($@"
DELETE FROM [Genres]
WHERE [Name] = N'{name}'
  AND [ParentGenreId] IS NULL
  AND NOT EXISTS (SELECT 1 FROM [GenreWork] WHERE [GenresId] = [Genres].[Id]);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in FormatGenres)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Genres] WHERE [Name] = N'{name}' AND [ParentGenreId] IS NULL)
    INSERT INTO [Genres] ([Name], [ParentGenreId]) VALUES (N'{name}', NULL);");
            }

            migrationBuilder.DeleteData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
