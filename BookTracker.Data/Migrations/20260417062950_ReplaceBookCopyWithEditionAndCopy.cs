using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceBookCopyWithEditionAndCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create new tables
            migrationBuilder.CreateTable(
                name: "Editions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Isbn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    DatePrinted = table.Column<DateOnly>(type: "date", nullable: true),
                    CoverUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublisherId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Editions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Editions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Editions_Publishers_PublisherId",
                        column: x => x.PublisherId,
                        principalTable: "Publishers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Copies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EditionId = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    DateAcquired = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Copies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Copies_Editions_EditionId",
                        column: x => x.EditionId,
                        principalTable: "Editions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Editions_BookId",
                table: "Editions",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_Editions_Isbn",
                table: "Editions",
                column: "Isbn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Editions_PublisherId",
                table: "Editions",
                column: "PublisherId");

            migrationBuilder.CreateIndex(
                name: "IX_Copies_EditionId",
                table: "Copies",
                column: "EditionId");

            // 2. Migrate data: each BookCopy becomes one Edition + one Copy.
            // BookCopies with the same ISBN get merged into one Edition with
            // multiple Copies (to respect the unique ISBN constraint).
            migrationBuilder.Sql(@"
                -- Create one Edition per distinct ISBN+BookId combination
                INSERT INTO Editions (BookId, Isbn, Format, DatePrinted, CoverUrl, PublisherId)
                SELECT BookId, Isbn, Format, DatePrinted, CustomCoverArtUrl, PublisherId
                FROM BookCopies bc
                WHERE bc.Id = (
                    SELECT MIN(bc2.Id)
                    FROM BookCopies bc2
                    WHERE bc2.Isbn = bc.Isbn AND bc2.BookId = bc.BookId
                );

                -- Create one Copy per original BookCopy, linked to its Edition
                INSERT INTO Copies (EditionId, Condition, DateAcquired, Notes)
                SELECT e.Id, bc.Condition, NULL, NULL
                FROM BookCopies bc
                INNER JOIN Editions e ON e.Isbn = bc.Isbn AND e.BookId = bc.BookId;
            ");

            // 3. Drop old table
            migrationBuilder.DropTable(
                name: "BookCopies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate BookCopies
            migrationBuilder.CreateTable(
                name: "BookCopies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    PublisherId = table.Column<int>(type: "int", nullable: true),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    CustomCoverArtUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DatePrinted = table.Column<DateOnly>(type: "date", nullable: true),
                    Format = table.Column<int>(type: "int", nullable: false),
                    Isbn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookCopies_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookCopies_Publishers_PublisherId",
                        column: x => x.PublisherId,
                        principalTable: "Publishers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Migrate data back: flatten Edition+Copy into BookCopies
            migrationBuilder.Sql(@"
                INSERT INTO BookCopies (BookId, Isbn, Format, DatePrinted, CustomCoverArtUrl, PublisherId, Condition)
                SELECT e.BookId, e.Isbn, e.Format, e.DatePrinted, e.CoverUrl, e.PublisherId, c.Condition
                FROM Copies c
                INNER JOIN Editions e ON e.Id = c.EditionId;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_BookCopies_BookId",
                table: "BookCopies",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookCopies_Isbn",
                table: "BookCopies",
                column: "Isbn");

            migrationBuilder.CreateIndex(
                name: "IX_BookCopies_PublisherId",
                table: "BookCopies",
                column: "PublisherId");

            // Drop new tables
            migrationBuilder.DropTable(
                name: "Copies");

            migrationBuilder.DropTable(
                name: "Editions");
        }
    }
}
