using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace duetGPT.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerToDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "General",
                table: "Documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Documents",
                type: "text",
                nullable: true);  // Allow null temporarily

            // Set a default owner for existing documents
            migrationBuilder.Sql(@"
                UPDATE ""Documents""
                SET ""OwnerId"" = (SELECT ""Id"" FROM ""AspNetUsers"" LIMIT 1)
                WHERE ""OwnerId"" IS NULL;
            ");

            // Now make the column not nullable
            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "Documents",
                type: "text",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerId",
                table: "Documents",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_AspNetUsers_OwnerId",
                table: "Documents",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_AspNetUsers_OwnerId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_OwnerId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "General",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Documents");
        }
    }
}
