using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeriesOnBookExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Books",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesOrder",
                table: "Books",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesOrderDisplay",
                table: "Books",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Expand phase: back-fill the new Book-level series columns from the
            // legacy Work-level membership. Series membership is a per-Book concept
            // now (a Book is installment N of a publication series), but historically
            // it lived on Work. For each Book, take the series of its constituent
            // Works. A Book *can* hold multiple Works, so the rank picks deterministically:
            // lowest SeriesOrder first (NULLs last), then lowest Work Id. On current
            // data this is unambiguous (every series-bearing Work sits in its own Book),
            // but the tie-break keeps the move safe if prod ever holds a conflict.
            // Works retain their series columns until the contract migration drops them.
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
JOIN    ranked r ON r.BooksId = b.Id AND r.rn = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Books_SeriesId",
                table: "Books",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Series_SeriesId",
                table: "Books",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Series_SeriesId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_SeriesId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesOrder",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesOrderDisplay",
                table: "Books");
        }
    }
}
