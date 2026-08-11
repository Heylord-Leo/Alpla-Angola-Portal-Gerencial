using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateBasedApprovalModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedQuotationItemId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SelectedCandidateId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WinnerSelectedAtUtc",
                table: "ApprovalBatchItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WinnerSelectedByUserId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinnerSelectionJustification",
                table: "ApprovalBatchItems",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalBatchItemCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SupplierNifSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    QuotedDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QuotedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    UnitTextSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IvaRatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IvaAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossSubtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QuotationDocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QuotationDocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasReconciliationWarnings = table.Column<bool>(type: "bit", nullable: false),
                    ReconciliationStatusSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReconciliationJustificationSnapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LineAdjustmentJustificationSnapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BuyerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBatchItemCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItemCandidates_ApprovalBatchItems_ApprovalBatchItemId",
                        column: x => x.ApprovalBatchItemId,
                        principalTable: "ApprovalBatchItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItemCandidates_QuotationItems_QuotationItemId",
                        column: x => x.QuotationItemId,
                        principalTable: "QuotationItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalBatchItemCandidates_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItems_SelectedCandidateId",
                table: "ApprovalBatchItems",
                column: "SelectedCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItemCandidate_BatchItemId",
                table: "ApprovalBatchItemCandidates",
                column: "ApprovalBatchItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItemCandidate_QuotationItemId",
                table: "ApprovalBatchItemCandidates",
                column: "QuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBatchItemCandidates_QuotationId",
                table: "ApprovalBatchItemCandidates",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalBatchItemCandidate_Item_QuotationItem",
                table: "ApprovalBatchItemCandidates",
                columns: new[] { "ApprovalBatchItemId", "QuotationItemId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalBatchItems_ApprovalBatchItemCandidates_SelectedCandidateId",
                table: "ApprovalBatchItems",
                column: "SelectedCandidateId",
                principalTable: "ApprovalBatchItemCandidates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalBatchItems_ApprovalBatchItemCandidates_SelectedCandidateId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropTable(
                name: "ApprovalBatchItemCandidates");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalBatchItems_SelectedCandidateId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "SelectedCandidateId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "WinnerSelectedAtUtc",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "WinnerSelectedByUserId",
                table: "ApprovalBatchItems");

            migrationBuilder.DropColumn(
                name: "WinnerSelectionJustification",
                table: "ApprovalBatchItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedQuotationItemId",
                table: "ApprovalBatchItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
