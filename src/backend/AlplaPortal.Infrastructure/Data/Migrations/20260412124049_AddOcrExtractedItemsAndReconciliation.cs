using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrExtractedItemsAndReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OcrExtractedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtractionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    RawDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RawUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedUnitId = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TaxRate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    QualityScore = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrExtractedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrExtractedItems_RequestAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "RequestAttachments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OcrExtractedItems_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OcrExtractedItems_Units_ResolvedUnitId",
                        column: x => x.ResolvedUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtractionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OcrExtractedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchConfidence = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MatchStrategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityDivergence = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UnitDivergence = table.Column<bool>(type: "bit", nullable: false),
                    BuyerReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BuyerJustification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationRecords_OcrExtractedItems_OcrExtractedItemId",
                        column: x => x.OcrExtractedItemId,
                        principalTable: "OcrExtractedItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationRecords_QuotationItems_QuotationItemId",
                        column: x => x.QuotationItemId,
                        principalTable: "QuotationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationRecords_RequestLineItems_RequesterItemId",
                        column: x => x.RequesterItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationRecords_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReconciliationRecords_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractedItems_AttachmentId",
                table: "OcrExtractedItems",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractedItems_ExtractionBatchId",
                table: "OcrExtractedItems",
                column: "ExtractionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractedItems_RequestId",
                table: "OcrExtractedItems",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractedItems_RequestId_ExtractionBatchId",
                table: "OcrExtractedItems",
                columns: new[] { "RequestId", "ExtractionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractedItems_ResolvedUnitId",
                table: "OcrExtractedItems",
                column: "ResolvedUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_ExtractionBatchId",
                table: "ReconciliationRecords",
                column: "ExtractionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_OcrExtractedItemId",
                table: "ReconciliationRecords",
                column: "OcrExtractedItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_QuotationItemId",
                table: "ReconciliationRecords",
                column: "QuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_RequesterItemId",
                table: "ReconciliationRecords",
                column: "RequesterItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_RequestId",
                table: "ReconciliationRecords",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_RequestId_ExtractionBatchId",
                table: "ReconciliationRecords",
                columns: new[] { "RequestId", "ExtractionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_ReviewedByUserId",
                table: "ReconciliationRecords",
                column: "ReviewedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationRecords");

            migrationBuilder.DropTable(
                name: "OcrExtractedItems");
        }
    }
}
