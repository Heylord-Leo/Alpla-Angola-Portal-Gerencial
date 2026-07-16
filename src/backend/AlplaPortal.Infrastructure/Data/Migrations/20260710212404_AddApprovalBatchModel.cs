using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalBatchModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalBatchId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotQuotedDecisionAtUtc",
                table: "RequestLineItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NotQuotedDecisionByUserId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotQuotedDecisionComment",
                table: "RequestLineItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotQuotedJustification",
                table: "RequestLineItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotQuotedProposedAtUtc",
                table: "RequestLineItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NotQuotedProposedByUserId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotationLifecycleStatus",
                table: "RequestLineItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovedTotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BudgetJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatches_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchExtraItemDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GeneratedRequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchExtraItemDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchExtraItemDecisions_ApprovalBatches_ApprovalBatchId",
                        column: x => x.ApprovalBatchId,
                        principalTable: "ApprovalBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchExtraItemDecisions_QuotationItems_QuotationItemId",
                        column: x => x.QuotationItemId,
                        principalTable: "QuotationItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalBatchExtraItemDecisions_RequestLineItems_GeneratedRequestLineItemId",
                        column: x => x.GeneratedRequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedQuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItems_ApprovalBatches_ApprovalBatchId",
                        column: x => x.ApprovalBatchId,
                        principalTable: "ApprovalBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItems_QuotationItems_SelectedQuotationItemId",
                        column: x => x.SelectedQuotationItemId,
                        principalTable: "QuotationItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItems_RequestLineItems_RequestLineItemId",
                        column: x => x.RequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_ApprovalBatchId",
                table: "RequestPoGroups",
                column: "ApprovalBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatch_Request_BatchNumber",
                table: "ApprovalBatches",
                columns: new[] { "RequestId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatch_RequestId",
                table: "ApprovalBatches",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchExtraItemDecision_BatchId",
                table: "ApprovalBatchExtraItemDecisions",
                column: "ApprovalBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchExtraItemDecisions_GeneratedRequestLineItemId",
                table: "ApprovalBatchExtraItemDecisions",
                column: "GeneratedRequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchExtraItemDecisions_QuotationItemId",
                table: "ApprovalBatchExtraItemDecisions",
                column: "QuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItem_BatchId",
                table: "ApprovalBatchItems",
                column: "ApprovalBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItem_LineItemId",
                table: "ApprovalBatchItems",
                column: "RequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItems_SelectedQuotationItemId",
                table: "ApprovalBatchItems",
                column: "SelectedQuotationItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestPoGroups_ApprovalBatches_ApprovalBatchId",
                table: "RequestPoGroups",
                column: "ApprovalBatchId",
                principalTable: "ApprovalBatches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestPoGroups_ApprovalBatches_ApprovalBatchId",
                table: "RequestPoGroups");

            migrationBuilder.DropTable(
                name: "ApprovalBatchExtraItemDecisions");

            migrationBuilder.DropTable(
                name: "ApprovalBatchItems");

            migrationBuilder.DropTable(
                name: "ApprovalBatches");

            migrationBuilder.DropIndex(
                name: "IX_RequestPoGroups_ApprovalBatchId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "ApprovalBatchId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "NotQuotedDecisionAtUtc",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "NotQuotedDecisionByUserId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "NotQuotedDecisionComment",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "NotQuotedJustification",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "NotQuotedProposedAtUtc",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "NotQuotedProposedByUserId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "QuotationLifecycleStatus",
                table: "RequestLineItems");
        }
    }
}
