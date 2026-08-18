using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationInvoiceAllocationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "OperationInvoiceAllocations",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "OperationInvoiceAllocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OperationInvoiceAllocations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "OperationInvoiceAllocations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "OperationInvoiceAllocations",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "OperationInvoiceAllocations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "OperationInvoiceAllocations");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OperationInvoiceAllocations");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "OperationInvoiceAllocations");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "OperationInvoiceAllocations");
        }
    }
}
