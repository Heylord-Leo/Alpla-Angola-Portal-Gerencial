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
            // Origin: NOT NULL with default 'MANUAL' — safe for existing rows
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "MANUAL");

            // SourceCompany: nullable — null for manually created suppliers
            migrationBuilder.AddColumn<string>(
                name: "SourceCompany",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            // LastSyncedAtUtc: nullable — null for manually created suppliers
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: true);
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
