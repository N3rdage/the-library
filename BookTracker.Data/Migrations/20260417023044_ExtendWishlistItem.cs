using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendWishlistItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Isbn",
                table: "WishlistItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "WishlistItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesOrder",
                table: "WishlistItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_Isbn",
                table: "WishlistItems",
                column: "Isbn");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_SeriesId",
                table: "WishlistItems",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_WishlistItems_Series_SeriesId",
                table: "WishlistItems",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WishlistItems_Series_SeriesId",
                table: "WishlistItems");

            migrationBuilder.DropIndex(
                name: "IX_WishlistItems_Isbn",
                table: "WishlistItems");

            migrationBuilder.DropIndex(
                name: "IX_WishlistItems_SeriesId",
                table: "WishlistItems");

            migrationBuilder.DropColumn(
                name: "Isbn",
                table: "WishlistItems");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "WishlistItems");

            migrationBuilder.DropColumn(
                name: "SeriesOrder",
                table: "WishlistItems");
        }
    }
}
