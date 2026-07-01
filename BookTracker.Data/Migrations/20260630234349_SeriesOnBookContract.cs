using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeriesOnBookContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Re-assert the Work→Book back-fill before dropping the source columns.
            // PR1's expand back-fill was a one-time snapshot; a series written to
            // Work-only in the window between the PR1 (expand) and PR2 (cutover)
            // deploys would leave Book.SeriesId null and then be lost when the Work
            // columns drop below. Fill only the stragglers (Book.SeriesId IS NULL) so
            // a correctly-set Book series is never clobbered. Same deterministic
            // tie-break as PR1 (lowest SeriesOrder, then Work Id).
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT  bw.BooksId,
            w.SeriesId,
            w.SeriesOrder,
            w.SeriesOrderDisplay,
            ROW_NUMBER() OVER (
                PARTITION BY bw.BooksId
                ORDER BY CASE WHEN w.SeriesOrder IS NULL THEN 1 ELSE 0 END,
                         w.SeriesOrder,
                         w.Id
            ) AS rn
    FROM    BookWork bw
    JOIN    Works    w ON w.Id = bw.WorksId
    WHERE   w.SeriesId IS NOT NULL
)
UPDATE  b
SET     b.SeriesId           = r.SeriesId,
        b.SeriesOrder        = r.SeriesOrder,
        b.SeriesOrderDisplay = r.SeriesOrderDisplay
FROM    Books  b
JOIN    ranked r ON r.BooksId = b.Id AND r.rn = 1
WHERE   b.SeriesId IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Works_Series_SeriesId",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_Works_SeriesId",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "SeriesOrder",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "SeriesOrderDisplay",
                table: "Works");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Works",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesOrder",
                table: "Works",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesOrderDisplay",
                table: "Works",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Reverse the cutover: restore Work-level series from the Book(s) each
            // Work is in (series now lives on the Book). Mirror of the PR1 expand
            // back-fill, in the opposite direction — deterministic tie-break (lowest
            // SeriesOrder, then Book Id) for a Work that spans multiple books/series.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT  bw.WorksId,
            b.SeriesId,
            b.SeriesOrder,
            b.SeriesOrderDisplay,
            ROW_NUMBER() OVER (
                PARTITION BY bw.WorksId
                ORDER BY CASE WHEN b.SeriesOrder IS NULL THEN 1 ELSE 0 END,
                         b.SeriesOrder,
                         b.Id
            ) AS rn
    FROM    BookWork bw
    JOIN    Books    b ON b.Id = bw.BooksId
    WHERE   b.SeriesId IS NOT NULL
)
UPDATE  w
SET     w.SeriesId           = r.SeriesId,
        w.SeriesOrder        = r.SeriesOrder,
        w.SeriesOrderDisplay = r.SeriesOrderDisplay
FROM    Works  w
JOIN    ranked r ON r.WorksId = w.Id AND r.rn = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Works_SeriesId",
                table: "Works",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Works_Series_SeriesId",
                table: "Works",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
