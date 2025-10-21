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

            // Check if embeddingcost column exists before adding it
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_name = 'ragdata'
                        AND column_name = 'embeddingcost'
                    ) THEN
                        ALTER TABLE ragdata ADD COLUMN embeddingcost numeric NULL;
                    END IF;
                END $$;
            ");

            // Note: KnowledgeResults and KnowledgeQueryResults are not database tables
            // They are non-persisted entities (HasNoKey) used only for query results
            // Therefore, no migration is needed for their Metadata properties
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embeddingcost",
                table: "ragdata");

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
