using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace duetGPT.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerToKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ownerid",
                table: "ragdata",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ragdata_ownerid",
                table: "ragdata",
                column: "ownerid");

            migrationBuilder.AddForeignKey(
                name: "FK_ragdata_AspNetUsers_ownerid",
                table: "ragdata",
                column: "ownerid",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ragdata_AspNetUsers_ownerid",
                table: "ragdata");

            migrationBuilder.DropIndex(
                name: "IX_ragdata_ownerid",
                table: "ragdata");

            migrationBuilder.DropColumn(
                name: "ownerid",
                table: "ragdata");
        }
    }
}
