using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace duetGPT.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleToThread : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Threads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Threads");
        }
    }
}
