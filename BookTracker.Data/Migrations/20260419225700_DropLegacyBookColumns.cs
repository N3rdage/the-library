using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyBookColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Series_SeriesId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_GenreWork_Works_WorkId",
                table: "GenreWork");

            migrationBuilder.DropTable(
                name: "BookGenre");

            migrationBuilder.DropIndex(
                name: "IX_Books_SeriesId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesOrder",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Subtitle",
                table: "Books");

            migrationBuilder.RenameColumn(
                name: "WorkId",
                table: "GenreWork",
                newName: "WorksId");

            migrationBuilder.RenameIndex(
                name: "IX_GenreWork_WorkId",
                table: "GenreWork",
                newName: "IX_GenreWork_WorksId");

            migrationBuilder.AddForeignKey(
                name: "FK_GenreWork_Works_WorksId",
                table: "GenreWork",
                column: "WorksId",
                principalTable: "Works",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GenreWork_Works_WorksId",
                table: "GenreWork");

            migrationBuilder.RenameColumn(
                name: "WorksId",
                table: "GenreWork",
                newName: "WorkId");

            migrationBuilder.RenameIndex(
                name: "IX_GenreWork_WorksId",
                table: "GenreWork",
                newName: "IX_GenreWork_WorkId");

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Books",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

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
                name: "Subtitle",
                table: "Books",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookGenre",
                columns: table => new
                {
                    BooksId = table.Column<int>(type: "int", nullable: false),
                    GenresId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookGenre", x => new { x.BooksId, x.GenresId });
                    table.ForeignKey(
                        name: "FK_BookGenre_Books_BooksId",
                        column: x => x.BooksId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookGenre_Genres_GenresId",
                        column: x => x.GenresId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_SeriesId",
                table: "Books",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_BookGenre_GenresId",
                table: "BookGenre",
                column: "GenresId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Series_SeriesId",
                table: "Books",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GenreWork_Works_WorkId",
                table: "GenreWork",
                column: "WorkId",
                principalTable: "Works",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
