using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLineItemStatusAndPriorityCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent, defensive migration for ItemPriority int->string.
            // Handles both cases: column still 'int' (clean DB) or already 'nvarchar' (partial prior run).
            // Uses a temp column pattern to fully control the transformation.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID('RequestLineItems')
                    AND name = 'ItemPriority_New'
                )
                BEGIN
                    ALTER TABLE [RequestLineItems] ADD [ItemPriority_New] nvarchar(max) NOT NULL CONSTRAINT DF_ItemPriorityNew DEFAULT 'MEDIUM';
                    ALTER TABLE [RequestLineItems] DROP CONSTRAINT DF_ItemPriorityNew;
                END;
            ");

            // Populate temp column: handles both int values and already-migrated string values
            migrationBuilder.Sql(@"
                UPDATE [RequestLineItems]
                SET [ItemPriority_New] =
                    CASE
                        -- Already valid codes
                        WHEN CAST([ItemPriority] AS nvarchar(10)) IN ('HIGH', 'MEDIUM', 'LOW') THEN CAST([ItemPriority] AS nvarchar(10))
                        -- Legacy int codes (only possible if column is still int)
                        ELSE 'MEDIUM'
                    END;
            ");

            // Drop old column and rename new column only if old still exists
            // Must drop default constraint first (SQL Server requirement)
            migrationBuilder.Sql(@"
                DECLARE @constraint_name NVARCHAR(256);
                SELECT @constraint_name = dc.name
                FROM sys.default_constraints dc
                JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE c.object_id = OBJECT_ID('RequestLineItems') AND c.name = 'ItemPriority';
                IF @constraint_name IS NOT NULL
                BEGIN
                    EXEC('ALTER TABLE [RequestLineItems] DROP CONSTRAINT [' + @constraint_name + ']');
                END;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID('RequestLineItems')
                    AND name = 'ItemPriority'
                )
                BEGIN
                    ALTER TABLE [RequestLineItems] DROP COLUMN [ItemPriority];
                END;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID('RequestLineItems')
                    AND name = 'ItemPriority_New'
                )
                BEGIN
                    EXEC sp_rename '[RequestLineItems].[ItemPriority_New]', 'ItemPriority', 'COLUMN';
                END;
            ");

            // EF Core schema snapshot update (no-op on DB, required for EF model consistency)
            migrationBuilder.AlterColumn<string>(
                name: "ItemPriority",
                table: "RequestLineItems",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "LineItemStatusId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LineItemStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BadgeColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineItemStatuses", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "LineItemStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "blue", "WAITING_QUOTATION", 1, true, "Aguardando Cotação" },
                    { 2, "yellow", "PENDING", 2, true, "Pendente" },
                    { 3, "indigo", "UNDER_REVIEW", 3, true, "Em Análise" },
                    { 4, "cyan", "ORDERED", 4, true, "Encomendado" },
                    { 5, "orange", "PARTIALLY_RECEIVED", 5, true, "Recebido Parcial" },
                    { 6, "green", "RECEIVED", 6, true, "Recebido" },
                    { 7, "red", "CANCELLED", 7, true, "Cancelado" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_LineItemStatusId",
                table: "RequestLineItems",
                column: "LineItemStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_LineItemStatuses_Code",
                table: "LineItemStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_LineItemStatuses_LineItemStatusId",
                table: "RequestLineItems",
                column: "LineItemStatusId",
                principalTable: "LineItemStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_LineItemStatuses_LineItemStatusId",
                table: "RequestLineItems");

            migrationBuilder.DropTable(
                name: "LineItemStatuses");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_LineItemStatusId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "LineItemStatusId",
                table: "RequestLineItems");

            migrationBuilder.AlterColumn<int>(
                name: "ItemPriority",
                table: "RequestLineItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
