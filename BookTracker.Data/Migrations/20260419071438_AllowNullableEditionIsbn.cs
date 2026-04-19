using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowNullableEditionIsbn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Editions_Isbn",
                table: "Editions");

            migrationBuilder.AlterColumn<string>(
                name: "Isbn",
                table: "Editions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_Editions_Isbn",
                table: "Editions",
                column: "Isbn",
                unique: true,
                filter: "[Isbn] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Editions_Isbn",
                table: "Editions");

            migrationBuilder.AlterColumn<string>(
                name: "Isbn",
                table: "Editions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Editions_Isbn",
                table: "Editions",
                column: "Isbn",
                unique: true);
        }
    }
}
