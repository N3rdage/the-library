using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceAuthorEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the Authors table (CanonicalAuthorId self-FK left
            //    untouched at this point — every seeded Author is canonical).
            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CanonicalAuthorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Authors_Authors_CanonicalAuthorId",
                        column: x => x.CanonicalAuthorId,
                        principalTable: "Authors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Authors_CanonicalAuthorId",
                table: "Authors",
                column: "CanonicalAuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Authors_Name",
                table: "Authors",
                column: "Name",
                unique: true);

            // 2. Add Works.AuthorId as nullable for the seed step. We make
            //    it non-null + add the FK after every Work has a value.
            migrationBuilder.AddColumn<int>(
                name: "AuthorId",
                table: "Works",
                type: "int",
                nullable: true);

            // 3. Seed one canonical Author per distinct Work.Author string.
            //    Existing data has Work.Author as a NOT NULL nvarchar(200);
            //    duplicates become a single canonical row, so two Bachman
            //    novels collapse into one Author. (Aliasing happens later
            //    via the /authors UI.)
            migrationBuilder.Sql(@"
INSERT INTO [Authors] ([Name], [CanonicalAuthorId])
SELECT DISTINCT [Author], NULL
FROM [Works]
WHERE NOT EXISTS (SELECT 1 FROM [Authors] a WHERE a.[Name] = [Works].[Author]);");

            // 4. Link every Work to its matching Author by name.
            migrationBuilder.Sql(@"
UPDATE [Works]
SET [AuthorId] = (SELECT TOP 1 a.[Id] FROM [Authors] a WHERE a.[Name] = [Works].[Author]);");

            // 5. Drop the legacy string column now that AuthorId is fully
            //    populated, then make AuthorId non-nullable + add the FK +
            //    index. The Restrict delete behaviour matches the EF model.
            migrationBuilder.DropColumn(
                name: "Author",
                table: "Works");

            migrationBuilder.AlterColumn<int>(
                name: "AuthorId",
                table: "Works",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the Author column from the linked Author entity.
            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Works",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE [Works]
SET [Author] = (SELECT a.[Name] FROM [Authors] a WHERE a.[Id] = [Works].[AuthorId]);");

            migrationBuilder.DropForeignKey(
                name: "FK_Works_Authors_AuthorId",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_Works_AuthorId",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Works");

            migrationBuilder.DropTable(
                name: "Authors");
        }
    }
}
