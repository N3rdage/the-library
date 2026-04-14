using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCoverArtUrl",
                table: "Books",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookCopies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Isbn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    DatePrinted = table.Column<DateOnly>(type: "date", nullable: true),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    CustomCoverArtUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookCopies_BookId",
                table: "BookCopies",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookCopies_Isbn",
                table: "BookCopies",
                column: "Isbn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookCopies");

            migrationBuilder.DropColumn(
                name: "DefaultCoverArtUrl",
                table: "Books");
        }
    }
}
