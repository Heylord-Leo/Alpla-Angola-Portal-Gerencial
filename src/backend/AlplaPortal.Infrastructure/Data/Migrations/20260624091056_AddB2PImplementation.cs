using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddB2PImplementation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvancePaymentPercent",
                table: "Requests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentConditionCode",
                table: "Requests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PaymentSequence = table.Column<int>(type: "int", nullable: false),
                    PlannedPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    PlannedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ScheduledDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ScheduledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActualPaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PaidDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PaymentProofAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HasDivergence = table.Column<bool>(type: "bit", nullable: false),
                    DivergenceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DivergenceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPayments_RequestAttachments_PaymentProofAttachmentId",
                        column: x => x.PaymentProofAttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RequestPayments_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestPayments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestPayments_Users_PaidByUserId",
                        column: x => x.PaidByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestPayments_Users_ScheduledByUserId",
                        column: x => x.ScheduledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestReconciliations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciliationSequence = table.Column<int>(type: "int", nullable: false),
                    ReconciliationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FinalInvoiceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FinalAcceptedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DeliveredAcceptedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DifferenceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DifferenceReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RemainingBalanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreditNoteRequired = table.Column<bool>(type: "bit", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreditNoteAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebitNoteRequired = table.Column<bool>(type: "bit", nullable: false),
                    DebitNoteNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DebitNoteAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundRequired = table.Column<bool>(type: "bit", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RefundStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompensationFuturePayment = table.Column<bool>(type: "bit", nullable: false),
                    CompensationNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReconciliationDecision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReconciliationNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestReconciliations_RequestAttachments_CreditNoteAttachmentId",
                        column: x => x.CreditNoteAttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestReconciliations_RequestAttachments_DebitNoteAttachmentId",
                        column: x => x.DebitNoteAttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestReconciliations_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestReconciliations_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestReconciliations_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 23, "#ffc107", "ADVANCE_PAYMENT_REQUIRED", 23, true, "Adiantamento Necessário" },
                    { 24, "#28a745", "ADVANCE_PAYMENT_COMPLETED", 24, true, "Adiantamento Realizado" },
                    { 25, "#6f42c1", "WAITING_SUPPLIER_DELIVERY", 25, true, "Ag. Entrega/Serviço" },
                    { 26, "#fd7e14", "WAITING_RECONCILIATION", 26, true, "Ag. Reconciliação" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_CreatedByUserId",
                table: "RequestPayments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_PaidByUserId",
                table: "RequestPayments",
                column: "PaidByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_PaymentProofAttachmentId",
                table: "RequestPayments",
                column: "PaymentProofAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_RequestId",
                table: "RequestPayments",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_RequestId_Type_Seq",
                table: "RequestPayments",
                columns: new[] { "RequestId", "PaymentType", "PaymentSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_ScheduledByUserId",
                table: "RequestPayments",
                column: "ScheduledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_CompletedByUserId",
                table: "RequestReconciliations",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_CreditNoteAttachmentId",
                table: "RequestReconciliations",
                column: "CreditNoteAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_DebitNoteAttachmentId",
                table: "RequestReconciliations",
                column: "DebitNoteAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_RequestId",
                table: "RequestReconciliations",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_RequestId_Seq",
                table: "RequestReconciliations",
                columns: new[] { "RequestId", "ReconciliationSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_StartedByUserId",
                table: "RequestReconciliations",
                column: "StartedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPayments");

            migrationBuilder.DropTable(
                name: "RequestReconciliations");

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DropColumn(
                name: "AdvancePaymentPercent",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "PaymentConditionCode",
                table: "Requests");
        }
    }
}
