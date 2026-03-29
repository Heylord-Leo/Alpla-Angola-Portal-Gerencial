using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIvaAndQuotationFinancials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalDiscountAmount",
                table: "Quotations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalGrossAmount",
                table: "Quotations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalIvaAmount",
                table: "Quotations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTaxableBase",
                table: "Quotations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "QuotationItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossSubtotal",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaAmount",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "IvaRateId",
                table: "QuotationItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaRatePercent",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "QuotationItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableBase",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "IvaRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IvaRates", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "IvaRates",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name", "RatePercent" },
                values: new object[,]
                {
                    { 1, "IVA_14", 1, true, "IVA 14%", 14.0m },
                    { 2, "IVA_7", 2, true, "IVA 7%", 7.0m },
                    { 3, "IVA_5", 3, true, "IVA 5%", 5.0m },
                    { 4, "IVA_3", 4, true, "IVA 3%", 3.0m },
                    { 5, "IVA_0", 5, true, "Isento (0%)", 0.0m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_IvaRateId",
                table: "QuotationItems",
                column: "IvaRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItems_IvaRates_IvaRateId",
                table: "QuotationItems",
                column: "IvaRateId",
                principalTable: "IvaRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_IvaRates_IvaRateId",
                table: "QuotationItems");

            migrationBuilder.DropTable(
                name: "IvaRates");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_IvaRateId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "TotalDiscountAmount",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "TotalGrossAmount",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "TotalIvaAmount",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "TotalTaxableBase",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "GrossSubtotal",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "IvaAmount",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "IvaRateId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "IvaRatePercent",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "TaxableBase",
                table: "QuotationItems");
        }
    }
}
