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
            // Idempotent Tag seeding. The original auto-generated `InsertData`
            // crashed on environments where a previous deploy attempt had
            // already inserted rows at Id 2/3 but the migration-history row
            // never landed (the slot-swap warmup runs migrations against
            // prod DB with prod config; if the warmup ping then fails, the
            // app dies before the migration history is consistent across
            // retries). Guarding by Id makes this safe to re-run.
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT [Tags] ON;
IF NOT EXISTS (SELECT 1 FROM [Tags] WHERE [Id] = 2)
    INSERT INTO [Tags] ([Id], [Name]) VALUES (2, N'format:graphic-novel');
IF NOT EXISTS (SELECT 1 FROM [Tags] WHERE [Id] = 3)
    INSERT INTO [Tags] ([Id], [Name]) VALUES (3, N'format:short-stories');
SET IDENTITY_INSERT [Tags] OFF;");

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
