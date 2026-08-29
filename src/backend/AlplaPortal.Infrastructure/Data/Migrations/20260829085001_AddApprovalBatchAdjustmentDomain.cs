using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalBatchAdjustmentDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalBatchAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    SourceStage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WholeBatch = table.Column<bool>(type: "bit", nullable: false),
                    ApproverComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalsBeforeMin = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalsBeforeMax = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustments_ApprovalBatches_ApprovalBatchId",
                        column: x => x.ApprovalBatchId,
                        principalTable: "ApprovalBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchAdjustmentFieldChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchAdjustmentFieldChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustmentFieldChanges_ApprovalBatchAdjustments_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "ApprovalBatchAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustmentFieldChanges_RequestLineItems_RequestLineItemId",
                        column: x => x.RequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchAdjustmentReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchAdjustmentReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustmentReasons_ApprovalBatchAdjustments_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "ApprovalBatchAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustmentReasons_RequestLineItems_RequestLineItemId",
                        column: x => x.RequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchAdjustmentResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchAdjustmentResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchAdjustmentResolutions_ApprovalBatchAdjustments_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "ApprovalBatchAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBatchCandidateReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchCandidateReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchCandidateReviews_ApprovalBatchAdjustments_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "ApprovalBatchAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchAdjustmentFieldChange_AdjustmentId",
                table: "ApprovalBatchAdjustmentFieldChanges",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchAdjustmentFieldChanges_RequestLineItemId",
                table: "ApprovalBatchAdjustmentFieldChanges",
                column: "RequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchAdjustmentReasons_RequestLineItemId",
                table: "ApprovalBatchAdjustmentReasons",
                column: "RequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchAdjustmentReason_Cycle_Code_Item",
                table: "ApprovalBatchAdjustmentReasons",
                columns: new[] { "AdjustmentId", "ReasonCode", "RequestLineItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchAdjustmentResolution_Cycle_Actor",
                table: "ApprovalBatchAdjustmentResolutions",
                columns: new[] { "AdjustmentId", "ActorType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchAdjustment_Batch_Cycle",
                table: "ApprovalBatchAdjustments",
                columns: new[] { "ApprovalBatchId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchAdjustment_OpenCycle",
                table: "ApprovalBatchAdjustments",
                column: "ApprovalBatchId",
                unique: true,
                filter: "[Status] IN (N'WAITING_REQUESTER', N'WAITING_BUYER')");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchCandidateReview_Cycle_Item_QuotationItem",
                table: "ApprovalBatchCandidateReviews",
                columns: new[] { "AdjustmentId", "ApprovalBatchItemId", "QuotationItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalBatchAdjustmentFieldChanges");

            migrationBuilder.DropTable(
                name: "ApprovalBatchAdjustmentReasons");

            migrationBuilder.DropTable(
                name: "ApprovalBatchAdjustmentResolutions");

            migrationBuilder.DropTable(
                name: "ApprovalBatchCandidateReviews");

            migrationBuilder.DropTable(
                name: "ApprovalBatchAdjustments");
        }
    }
}
