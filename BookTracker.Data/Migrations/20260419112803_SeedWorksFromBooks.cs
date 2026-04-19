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
            //
            // The whole step runs as a single SQL batch so the @Mapping
            // table-variable survives across the three statements. EF Core
            // wraps the migration in a transaction by default so a failure
            // anywhere rolls back to a clean state.
            //
            // Earlier shape used a Title/Author tuple JOIN to link Books to
            // their newly-inserted Works. That over-matched whenever the
            // library contained two Books with the same title (e.g. two
            // separate printings of the same Christie novel entered under
            // distinct ISBNs), producing duplicate (WorkId, GenresId) pairs
            // in step 3 and a PK_GenreWork violation. The MERGE...OUTPUT
            // pattern below maps each Book to a freshly-inserted Work
            // 1:1 by construction, regardless of title duplication.
            migrationBuilder.Sql(@"
DECLARE @Mapping TABLE (BookId INT, WorkId INT);

MERGE INTO [Works] AS target
USING (
    SELECT b.[Id] AS BookId, b.[Title], b.[Subtitle], b.[Author],
           b.[SeriesId], b.[SeriesOrder]
    FROM [Books] b
    WHERE NOT EXISTS (SELECT 1 FROM [BookWork] bw WHERE bw.[BooksId] = b.[Id])
) AS source
ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT ([Title], [Subtitle], [Author], [SeriesId], [SeriesOrder], [FirstPublishedDate])
    VALUES (source.[Title], source.[Subtitle], source.[Author],
            source.[SeriesId], source.[SeriesOrder], NULL)
OUTPUT source.BookId, inserted.[Id] INTO @Mapping (BookId, WorkId);

INSERT INTO [BookWork] ([BooksId], [WorksId])
SELECT BookId, WorkId FROM @Mapping;

INSERT INTO [GenreWork] ([WorkId], [GenresId])
SELECT DISTINCT m.WorkId, bg.[GenresId]
FROM @Mapping m
JOIN [BookGenre] bg ON bg.[BooksId] = m.BookId
WHERE NOT EXISTS (
    SELECT 1 FROM [GenreWork] gw
    WHERE gw.[WorkId] = m.WorkId AND gw.[GenresId] = bg.[GenresId]
);
");
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
