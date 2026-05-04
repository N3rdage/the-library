using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropWorkAuthorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Works_Authors_AuthorId",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_Works_AuthorId",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Works");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorId",
                table: "Works",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Reseed AuthorId from WorkAuthors (lead author by Order ascending)
            // so the column carries valid FK values before the FK is restored.
            // Defensive — Down() is only run for emergency rollback, but if
            // it runs after PR2 ships, FK creation needs valid AuthorIds for
            // every row. Every Work has at least one WorkAuthor row post-PR1
            // backfill; rows without one (impossible by current invariants)
            // would block the FK creation here.
            migrationBuilder.Sql(@"
UPDATE [Works]
SET [AuthorId] = (
    SELECT TOP 1 wa.[AuthorId]
    FROM [WorkAuthors] wa
    WHERE wa.[WorkId] = [Works].[Id]
    ORDER BY wa.[Order]
);");

            migrationBuilder.CreateIndex(
                name: "IX_Works_AuthorId",
                table: "Works",
                column: "AuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Works_Authors_AuthorId",
                table: "Works",
                column: "AuthorId",
                principalTable: "Authors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
