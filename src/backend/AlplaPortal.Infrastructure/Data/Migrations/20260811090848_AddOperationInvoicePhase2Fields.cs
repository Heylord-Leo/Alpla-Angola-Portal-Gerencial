using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationInvoicePhase2Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "OperationInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OperationInvoices",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "OperationInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "OperationInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "OperationInvoices",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "OperationInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "OperationInvoices",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "OperationInvoices");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "OperationInvoices");
        }
    }
}
