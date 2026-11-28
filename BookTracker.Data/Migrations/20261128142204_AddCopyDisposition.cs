using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCopyDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "Copies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RemovalNote",
                table: "Copies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RemovedOn",
                table: "Copies",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "Copies");

            migrationBuilder.DropColumn(
                name: "RemovalNote",
                table: "Copies");

            migrationBuilder.DropColumn(
                name: "RemovedOn",
                table: "Copies");
        }
    }
}
