using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Fixes snapshot-vs-database desync: the QuotationItem entity already maps
    /// ItemCatalogId (nullable FK to ItemCatalogItems), and the model snapshot
    /// includes the column, but no prior migration ever added it to the
    /// QuotationItems table. This migration bridges the gap.
    /// </summary>
    public partial class AddItemCatalogToQuotationItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: column, index, and FK are only created if they don't already exist.

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'QuotationItems' AND COLUMN_NAME = 'ItemCatalogId'
)
BEGIN
    ALTER TABLE [QuotationItems] ADD [ItemCatalogId] int NULL;
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_QuotationItems_ItemCatalogId' AND object_id = OBJECT_ID('QuotationItems')
)
BEGIN
    CREATE INDEX [IX_QuotationItems_ItemCatalogId] ON [QuotationItems] ([ItemCatalogId]);
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_QuotationItems_ItemCatalogItems_ItemCatalogId'
)
BEGIN
    ALTER TABLE [QuotationItems] ADD CONSTRAINT [FK_QuotationItems_ItemCatalogItems_ItemCatalogId]
        FOREIGN KEY ([ItemCatalogId]) REFERENCES [ItemCatalogItems] ([Id]) ON DELETE SET NULL;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_ItemCatalogItems_ItemCatalogId",
                table: "QuotationItems");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_ItemCatalogId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "ItemCatalogId",
                table: "QuotationItems");
        }
    }
}

