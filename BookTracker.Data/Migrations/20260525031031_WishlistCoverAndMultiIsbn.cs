using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class WishlistCoverAndMultiIsbn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverUrl",
                table: "WishlistItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WishlistItemIsbns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WishlistItemId = table.Column<int>(type: "int", nullable: false),
                    Isbn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistItemIsbns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WishlistItemIsbns_WishlistItems_WishlistItemId",
                        column: x => x.WishlistItemId,
                        principalTable: "WishlistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItemIsbns_Isbn",
                table: "WishlistItemIsbns",
                column: "Isbn");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItemIsbns_WishlistItemId",
                table: "WishlistItemIsbns",
                column: "WishlistItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WishlistItemIsbns");

            migrationBuilder.DropColumn(
                name: "CoverUrl",
                table: "WishlistItems");
        }
    }
}
