using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAuthorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkAuthors",
                table: "WorkAuthors");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "WorkAuthors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkAuthors",
                table: "WorkAuthors",
                columns: new[] { "WorkId", "AuthorId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkAuthors",
                table: "WorkAuthors");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "WorkAuthors");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkAuthors",
                table: "WorkAuthors",
                columns: new[] { "WorkId", "AuthorId" });
        }
    }
}
