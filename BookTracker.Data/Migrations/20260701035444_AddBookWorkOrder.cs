using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookWorkOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "BookWork",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill capture order for existing rows: within each Book, number
            // its Works 0-based by WorksId ascending. WorksId is the identity Id,
            // so this is creation order — the closest proxy to the order the user
            // captured them in (there's no per-join timestamp to do better). New
            // single-work books trivially get 0; multi-work anthologies get a
            // deterministic sequence the user can then reorder.
            migrationBuilder.Sql(@"
                WITH ordered AS (
                    SELECT [BooksId], [WorksId],
                           ROW_NUMBER() OVER (PARTITION BY [BooksId] ORDER BY [WorksId]) - 1 AS rn
                    FROM [BookWork]
                )
                UPDATE bw
                SET bw.[Order] = ordered.rn
                FROM [BookWork] bw
                JOIN ordered
                  ON bw.[BooksId] = ordered.[BooksId]
                 AND bw.[WorksId] = ordered.[WorksId];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Order",
                table: "BookWork");
        }
    }
}
