using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace duetGPT.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                table: "Threads",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<decimal>(
                name: "embeddingcost",
                table: "ragdata",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "KnowledgeResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "KnowledgeQueryResults",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embeddingcost",
                table: "ragdata");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "KnowledgeResults");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "KnowledgeQueryResults");

            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                table: "Threads",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }
    }
}
