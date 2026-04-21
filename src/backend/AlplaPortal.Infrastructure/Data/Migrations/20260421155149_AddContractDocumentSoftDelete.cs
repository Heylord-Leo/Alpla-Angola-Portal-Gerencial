using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractDocumentSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Soft-delete support for ContractDocuments (Phase 1).
            // Physical files are NOT removed by this migration.
            // A background cleanup job will handle deferred physical deletion.
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ContractDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ContractDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "ContractDocuments",
                type: "uniqueidentifier",
                nullable: true);

            // Index to efficiently filter out soft-deleted rows in all document queries
            migrationBuilder.CreateIndex(
                name: "IX_ContractDocuments_IsDeleted",
                table: "ContractDocuments",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContractDocuments_IsDeleted",
                table: "ContractDocuments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ContractDocuments");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ContractDocuments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "ContractDocuments");
        }
    }
}
