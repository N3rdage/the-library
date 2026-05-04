using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkAuthors",
                columns: table => new
                {
                    WorkId = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAuthors", x => new { x.WorkId, x.AuthorId });
                    table.ForeignKey(
                        name: "FK_WorkAuthors_Authors_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAuthors_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkAuthors_AuthorId",
                table: "WorkAuthors",
                column: "AuthorId");

            // Backfill: every existing Work seeds one WorkAuthor row from its
            // legacy AuthorId, with Order = 0 (lead/sole author). Idempotent
            // via NOT EXISTS so re-running the migration on an already-
            // migrated DB is a no-op (matches patterns.md §6).
            migrationBuilder.Sql(@"
INSERT INTO [WorkAuthors] ([WorkId], [AuthorId], [Order])
SELECT [Id], [AuthorId], 0
FROM [Works]
WHERE NOT EXISTS (
    SELECT 1 FROM [WorkAuthors] wa
    WHERE wa.[WorkId] = [Works].[Id] AND wa.[AuthorId] = [Works].[AuthorId]
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkAuthors");
        }
    }
}
