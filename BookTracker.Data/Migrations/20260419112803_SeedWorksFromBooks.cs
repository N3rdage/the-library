using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedWorksFromBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: any Book that already has a linked Work in BookWork
            // is skipped. Safe to re-run; safe across deploys where some Books
            // were created post-Work-entity (those already have a Work via
            // WorkSync's dual-write).

            // 1. Insert one Work row per Book that doesn't yet have one.
            //    The synthesised Work mirrors the Book's title/subtitle/author
            //    plus its Series membership.
            migrationBuilder.Sql(@"
INSERT INTO [Works] ([Title], [Subtitle], [Author], [SeriesId], [SeriesOrder], [FirstPublishedDate])
SELECT b.[Title], b.[Subtitle], b.[Author], b.[SeriesId], b.[SeriesOrder], NULL
FROM [Books] b
WHERE NOT EXISTS (
    SELECT 1 FROM [BookWork] bw WHERE bw.[BooksId] = b.[Id]
);");

            // 2. Link each Book to its just-created Work via BookWork. We
            //    identify the freshly-inserted Work by matching on the
            //    Title/Subtitle/Author/SeriesId tuple. This works because
            //    step 1 only inserted Works for un-linked Books, and that
            //    tuple is unique per-Book in practice.
            migrationBuilder.Sql(@"
INSERT INTO [BookWork] ([BooksId], [WorksId])
SELECT b.[Id], w.[Id]
FROM [Books] b
JOIN [Works] w
    ON w.[Title] = b.[Title]
   AND w.[Author] = b.[Author]
   AND ISNULL(w.[Subtitle], N'') = ISNULL(b.[Subtitle], N'')
   AND ISNULL(w.[SeriesId], -1) = ISNULL(b.[SeriesId], -1)
WHERE NOT EXISTS (
    SELECT 1 FROM [BookWork] bw
    WHERE bw.[BooksId] = b.[Id] AND bw.[WorksId] = w.[Id]
);");

            // 3. Copy each (BookId, GenreId) pair into (WorkId, GenreId).
            //    Uses the BookWork link from step 2 to find the right Work
            //    for each Book.
            migrationBuilder.Sql(@"
INSERT INTO [GenreWork] ([WorkId], [GenresId])
SELECT bw.[WorksId], bg.[GenresId]
FROM [BookGenre] bg
JOIN [BookWork] bw ON bw.[BooksId] = bg.[BooksId]
WHERE NOT EXISTS (
    SELECT 1 FROM [GenreWork] gw
    WHERE gw.[WorkId] = bw.[WorksId] AND gw.[GenresId] = bg.[GenresId]
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort revert: clear out everything seeded. Only safe
            // before PR 2 lands (which drops the Book columns we read from).
            migrationBuilder.Sql(@"DELETE FROM [GenreWork];");
            migrationBuilder.Sql(@"DELETE FROM [BookWork];");
            migrationBuilder.Sql(@"DELETE FROM [Works];");
        }
    }
}
