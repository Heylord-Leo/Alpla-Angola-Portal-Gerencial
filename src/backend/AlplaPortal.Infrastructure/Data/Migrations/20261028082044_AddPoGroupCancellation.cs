using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPoGroupCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastFinanceReturnKind",
                table: "RequestPoGroups",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFinanceReturnReasonCode",
                table: "RequestPoGroups",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFinanceReturnedAtUtc",
                table: "RequestPoGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastFinanceReturnedByUserId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PoGroupCancellationId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseKind",
                table: "ApprovalBatchItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAtUtc",
                table: "ApprovalBatchItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleasedByUserId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPoCancelled",
                table: "AccountsPayableNotificationConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RequestPoGroupCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PreviousGroupStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PurchaseOrderNumberSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalAmountSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCodeSnapshot = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ApprovalBatchNumberSnapshot = table.Column<int>(type: "int", nullable: true),
                    OriginFinanceReturnHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinanceConfirmationRequired = table.Column<bool>(type: "bit", nullable: false),
                    EcfStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EcfReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EcfDeclarationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EcfEvidenceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EcfDeclaredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EcfDeclaredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinanceConfirmationStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FinanceConfirmedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinanceConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinanceConfirmationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FinanceReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinanceReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinanceRecipientsResolvedCount = table.Column<int>(type: "int", nullable: false),
                    FinanceRecipientsWithEmailCount = table.Column<int>(type: "int", nullable: false),
                    FinanceEmailsQueuedCount = table.Column<int>(type: "int", nullable: false),
                    FinanceEmailPolicyOutcome = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BuyerRecipientResolved = table.Column<bool>(type: "bit", nullable: false),
                    BuyerEmailQueued = table.Column<bool>(type: "bit", nullable: false),
                    LastNotificationCorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastNotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotificationResendCount = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPoGroupCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPoGroupCancellations_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPoGroupCancellations_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestPoGroupCancellationEcfEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupCancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EcfStatusAfter = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FinanceStatusAfter = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EvidenceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRoleSnapshot = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPoGroupCancellationEcfEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPoGroupCancellationEcfEvents_RequestPoGroupCancellations_RequestPoGroupCancellationId",
                        column: x => x.RequestPoGroupCancellationId,
                        principalTable: "RequestPoGroupCancellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestPoGroupCancellationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupCancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousLineItemStatusCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreviousQuotationLifecycleStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreviousSelectedQuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPoGroupCancellationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPoGroupCancellationItems_RequestLineItems_RequestLineItemId",
                        column: x => x.RequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPoGroupCancellationItems_RequestPoGroupCancellations_RequestPoGroupCancellationId",
                        column: x => x.RequestPoGroupCancellationId,
                        principalTable: "RequestPoGroupCancellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItem_PoGroupCancellationId",
                table: "ApprovalBatchItems",
                column: "PoGroupCancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellationEcfEvents_Cancellation",
                table: "RequestPoGroupCancellationEcfEvents",
                column: "RequestPoGroupCancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellationItems_Line",
                table: "RequestPoGroupCancellationItems",
                column: "RequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellationItems_RequestPoGroupCancellationId",
                table: "RequestPoGroupCancellationItems",
                column: "RequestPoGroupCancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellations_FinanceReview",
                table: "RequestPoGroupCancellations",
                columns: new[] { "FinanceConfirmationRequired", "FinanceConfirmationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellations_Group",
                table: "RequestPoGroupCancellations",
                column: "RequestPoGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroupCancellations_Request",
                table: "RequestPoGroupCancellations",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPoGroupCancellationEcfEvents");

            migrationBuilder.DropTable(
                name: "RequestPoGroupCancellationItems");

            migrationBuilder.DropTable(
                name: "RequestPoGroupCancellations");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalBatchItem_PoGroupCancellationId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "LastFinanceReturnKind",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "LastFinanceReturnReasonCode",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "LastFinanceReturnedAtUtc",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "LastFinanceReturnedByUserId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "PoGroupCancellationId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "ReleaseKind",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "ReleasedByUserId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "NotifyOnPoCancelled",
                table: "AccountsPayableNotificationConfigs");
        }
    }
}
