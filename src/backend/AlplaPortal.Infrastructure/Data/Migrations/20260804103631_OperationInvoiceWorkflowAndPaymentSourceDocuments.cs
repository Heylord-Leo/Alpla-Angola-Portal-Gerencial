using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationInvoiceWorkflowAndPaymentSourceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Hand-corrected. The scaffolder proposed three RenameColumn operations pairing
            // FinalInvoiceValidatedByUserId/AtUtc/RejectionReason with ExpectedTotalSetByUserId/
            // AtUtc/Justification purely because the types line up. They are unrelated columns: one
            // set recorded who validated a single invoice, the other records who set a group's
            // expected total by hand. A rename asserts continuity of meaning that does not exist,
            // so these are dropped and added instead.
            //
            // Every drop below is safe ONLY because nothing was ever written: the post-payment
            // workflow has been flag-disabled since Release 1. The guard makes that a fact rather
            // than an assumption — if any row exists, the migration refuses rather than destroying
            // evidence of a real payment.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [FinalInvoiceReconciliations])
                    THROW 51000, 'FinalInvoiceReconciliations is not empty. Release 3 assumes the post-payment workflow never ran. Restore from backup and migrate the data deliberately instead of dropping it.', 1;
                IF EXISTS (SELECT 1 FROM [RequestPoGroups] WHERE [FinalInvoiceAttachmentId] IS NOT NULL)
                    THROW 51000, 'RequestPoGroups carries operation-invoice attachments. Release 3 assumes none exist. Restore from backup and migrate the data deliberately.', 1;
            ");

            migrationBuilder.DropTable(
                name: "FinalInvoiceReconciliations");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceAttachmentId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceUploadedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceUploadedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceValidatedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceValidatedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "FinalInvoiceRejectionReason",
                table: "RequestPoGroups");

            migrationBuilder.AddColumn<Guid>(
                name: "ExpectedTotalSetByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedTotalSetAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedTotalJustification",
                table: "RequestPoGroups",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedOperationInvoiceCurrency",
                table: "RequestPoGroups",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedOperationInvoiceTotal",
                table: "RequestPoGroups",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlantId",
                table: "RequestPoGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentSourceDocumentId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperationInvoiceReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationInvoiceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperationInvoiceAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NifMatched = table.Column<bool>(type: "bit", nullable: false),
                    CompanyMatched = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationWarning = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AllocatedTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CumulativeValidatedTotalBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpectedTotalAtComparison = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_OperationInvoiceReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationInvoiceReconciliations_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierTaxIdSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BilledCompanyNameRead = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentSeries = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AmountsEnteredManually = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SupersededByOperationInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationInvoices_RequestAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationInvoices_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationInvoiceShortCloses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProposalJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EvidenceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemainingAmountAtProposal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationInvoiceShortCloses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationInvoiceShortCloses_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SupplierTaxIdSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlantId = table.Column<int>(type: "int", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentSeries = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OcrSuggestion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OcrConfidence = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    OcrEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OcrConflictingEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OcrTitleFound = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ClassificationSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ClassificationSuggestionSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ClassificationConflictAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClassificationReviewedByFinance = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentSourceDocuments_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentSourceDocuments_RequestAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentSourceDocuments_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentSourceDocuments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationInvoiceAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatedNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllocatedTaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllocatedGrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationInvoiceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationInvoiceAllocations_OperationInvoices_OperationInvoiceId",
                        column: x => x.OperationInvoiceId,
                        principalTable: "OperationInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OperationInvoiceAllocations_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationInvoiceAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaselineLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaselineLineType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MatchStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationInvoiceLines_OperationInvoiceAllocations_OperationInvoiceAllocationId",
                        column: x => x.OperationInvoiceAllocationId,
                        principalTable: "OperationInvoiceAllocations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationInvoiceLines_OperationInvoices_OperationInvoiceId",
                        column: x => x.OperationInvoiceId,
                        principalTable: "OperationInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroup_OperationInvoiceStatus",
                table: "RequestPoGroups",
                column: "OperationInvoiceStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_PlantId",
                table: "RequestPoGroups",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItem_PaymentSourceDocumentId",
                table: "RequestLineItems",
                column: "PaymentSourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "UX_OperationInvoiceAllocation_GroupSequence",
                table: "OperationInvoiceAllocations",
                columns: new[] { "RequestPoGroupId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OperationInvoiceAllocation_InvoiceGroup",
                table: "OperationInvoiceAllocations",
                columns: new[] { "OperationInvoiceId", "RequestPoGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceLine_AllocationId",
                table: "OperationInvoiceLines",
                column: "OperationInvoiceAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceLine_BaselineLineId",
                table: "OperationInvoiceLines",
                column: "BaselineLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceLines_OperationInvoiceId",
                table: "OperationInvoiceLines",
                column: "OperationInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceReconciliation_AllocationId",
                table: "OperationInvoiceReconciliations",
                column: "OperationInvoiceAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceReconciliation_AttachmentId",
                table: "OperationInvoiceReconciliations",
                column: "OperationInvoiceAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoiceReconciliation_PoGroupId",
                table: "OperationInvoiceReconciliations",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoice_RequestStatus",
                table: "OperationInvoices",
                columns: new[] { "RequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationInvoice_SupplierDocument",
                table: "OperationInvoices",
                columns: new[] { "SupplierId", "DocumentNumber", "DocumentSeries" });

            migrationBuilder.CreateIndex(
                name: "UX_OperationInvoice_AttachmentId",
                table: "OperationInvoices",
                column: "AttachmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OperationInvoiceShortClose_ActivePerGroup",
                table: "OperationInvoiceShortCloses",
                column: "RequestPoGroupId",
                unique: true,
                filter: "[Status] IN ('PROPOSED', 'APPROVED')");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSourceDocument_RequestActive",
                table: "PaymentSourceDocuments",
                columns: new[] { "RequestId", "IsVoided" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSourceDocument_SupplierDocument",
                table: "PaymentSourceDocuments",
                columns: new[] { "SupplierId", "DocumentNumber", "DocumentSeries" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSourceDocuments_PlantId",
                table: "PaymentSourceDocuments",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentSourceDocument_AttachmentId",
                table: "PaymentSourceDocuments",
                column: "AttachmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentSourceDocument_RequestSequence",
                table: "PaymentSourceDocuments",
                columns: new[] { "RequestId", "SequenceNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_PaymentSourceDocuments_PaymentSourceDocumentId",
                table: "RequestLineItems",
                column: "PaymentSourceDocumentId",
                principalTable: "PaymentSourceDocuments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestPoGroups_Plants_PlantId",
                table: "RequestPoGroups",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_PaymentSourceDocuments_PaymentSourceDocumentId",
                table: "RequestLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestPoGroups_Plants_PlantId",
                table: "RequestPoGroups");

            migrationBuilder.DropTable(
                name: "OperationInvoiceLines");

            migrationBuilder.DropTable(
                name: "OperationInvoiceReconciliations");

            migrationBuilder.DropTable(
                name: "OperationInvoiceShortCloses");

            migrationBuilder.DropTable(
                name: "PaymentSourceDocuments");

            migrationBuilder.DropTable(
                name: "OperationInvoiceAllocations");

            migrationBuilder.DropTable(
                name: "OperationInvoices");

            migrationBuilder.DropIndex(
                name: "IX_RequestPoGroup_OperationInvoiceStatus",
                table: "RequestPoGroups");

            migrationBuilder.DropIndex(
                name: "IX_RequestPoGroups_PlantId",
                table: "RequestPoGroups");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItem_PaymentSourceDocumentId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "ExpectedOperationInvoiceCurrency",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "ExpectedOperationInvoiceTotal",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "PlantId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "PaymentSourceDocumentId",
                table: "RequestLineItems");

            // Mirrors the hand-correction in Up(): these were never renames, so they are dropped
            // and the superseded columns re-added empty.
            migrationBuilder.DropColumn(
                name: "ExpectedTotalSetByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "ExpectedTotalSetAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "ExpectedTotalJustification",
                table: "RequestPoGroups");

            migrationBuilder.AddColumn<Guid>(
                name: "FinalInvoiceValidatedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalInvoiceValidatedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalInvoiceRejectionReason",
                table: "RequestPoGroups",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalInvoiceAttachmentId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

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

            migrationBuilder.CreateTable(
                name: "FinalInvoiceReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyMatched = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceAccepted = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceDetected = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalInvoiceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReconciliationDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResidualVariance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SupplierMatched = table.Column<bool>(type: "bit", nullable: false),
                    ToleranceApplied = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_FinalInvoiceReconciliation_AttachmentId",
                table: "FinalInvoiceReconciliations",
                column: "FinalInvoiceAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinalInvoiceReconciliation_PoGroupId",
                table: "FinalInvoiceReconciliations",
                column: "RequestPoGroupId");
        }
    }
}
