using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace duetGPT.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnsToRagdata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "ragdata",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "creationdate",
                table: "ragdata",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "title",
                table: "ragdata");

            migrationBuilder.DropColumn(
                name: "creationdate",
                table: "ragdata");
        }
    }
}
