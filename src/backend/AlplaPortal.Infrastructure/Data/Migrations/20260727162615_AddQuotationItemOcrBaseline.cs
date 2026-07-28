using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationItemOcrBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LineAdjustmentJustification",
                table: "QuotationItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OcrOriginalDiscountAmount",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OcrOriginalIvaRatePercent",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OcrOriginalLineTotal",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OcrOriginalQuantity",
                table: "QuotationItems",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OcrOriginalUnitId",
                table: "QuotationItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OcrOriginalUnitPrice",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OcrOriginalUnitText",
                table: "QuotationItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineAdjustmentJustification",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalDiscountAmount",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalIvaRatePercent",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalLineTotal",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalQuantity",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalUnitId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalUnitPrice",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "OcrOriginalUnitText",
                table: "QuotationItems");
        }
    }
}
