using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds Origin, SourceCompany, and LastSyncedAtUtc columns to the Suppliers table.
    /// These columns existed in the entity model and DbContext snapshot since v2.156.3
    /// but were never added by any migration — they were only present in the
    /// ConsolidatedBaseline (clean installs). Existing databases (like TEST) are missing them,
    /// causing "Invalid column name" SqlExceptions on any Supplier query.
    /// </summary>
    public partial class AddSupplierSyncColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: only add each column if it does not already exist.
            // This handles databases where ConsolidatedBaseline already created
            // these columns (e.g., Development created via clean install) but
            // this migration was never registered in __EFMigrationsHistory.

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Suppliers' AND COLUMN_NAME = 'Origin'
)
BEGIN
    ALTER TABLE [Suppliers] ADD [Origin] nvarchar(max) NOT NULL DEFAULT N'MANUAL';
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Suppliers' AND COLUMN_NAME = 'SourceCompany'
)
BEGIN
    ALTER TABLE [Suppliers] ADD [SourceCompany] nvarchar(max) NULL;
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Suppliers' AND COLUMN_NAME = 'LastSyncedAtUtc'
)
BEGIN
    ALTER TABLE [Suppliers] ADD [LastSyncedAtUtc] datetime2 NULL;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "SourceCompany",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "LastSyncedAtUtc",
                table: "Suppliers");
        }
    }
}
