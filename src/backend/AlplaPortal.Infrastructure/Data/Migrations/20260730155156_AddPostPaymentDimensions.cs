using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostPaymentDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "RequestStatusHistories",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingDocumentType",
                table: "Requests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletionCycleId",
                table: "Requests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Requests",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "BillingDocumentType",
                table: "RequestPoGroups",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalInvoiceAttachmentId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalInvoiceRejectionReason",
                table: "RequestPoGroups",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalInvoiceStatus",
                table: "RequestPoGroups",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UNCLASSIFIED");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalInvoiceUploadedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalInvoiceUploadedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalInvoiceValidatedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalInvoiceValidatedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FiscalReceiptAttachmentId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalReceiptUploadedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FiscalReceiptUploadedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OperationalReceiptCompletedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationalReceiptCompletedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RequestPoGroups",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "Quotations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinalInvoiceReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalInvoiceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InvoiceTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ResidualVariance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ToleranceApplied = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DivergenceDetected = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceAccepted = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SupplierMatched = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyMatched = table.Column<bool>(type: "bit", nullable: false),
                    ReconciliationDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalInvoiceReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalInvoiceReconciliations_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 29, "#8b5cf6", "WAITING_FISCAL_RECEIPT", 29, true, "Aguardando Recibo Fiscal" });

            migrationBuilder.CreateIndex(
                name: "UX_RequestStatusHistory_IdempotencyKey",
                table: "RequestStatusHistories",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinalInvoiceReconciliation_AttachmentId",
                table: "FinalInvoiceReconciliations",
                column: "FinalInvoiceAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinalInvoiceReconciliation_PoGroupId",
                table: "FinalInvoiceReconciliations",
                column: "RequestPoGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalInvoiceReconciliations");

            migrationBuilder.DropIndex(
                name: "UX_RequestStatusHistory_IdempotencyKey",
                table: "RequestStatusHistories");

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "RequestStatusHistories");

            migrationBuilder.DropColumn(
                name: "BillingDocumentType",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CompletionCycleId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "BillingDocumentType",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceAttachmentId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceRejectionReason",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceStatus",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceUploadedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceUploadedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceValidatedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceValidatedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FiscalReceiptAttachmentId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FiscalReceiptUploadedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FiscalReceiptUploadedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "OperationalReceiptCompletedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "OperationalReceiptCompletedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "Quotations");
        }
    }
}
