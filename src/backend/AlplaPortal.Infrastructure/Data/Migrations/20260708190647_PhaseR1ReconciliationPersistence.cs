using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseR1ReconciliationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReconciliationJustification",
                table: "QuotationItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationStatus",
                table: "QuotationItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "MAPPED");

            migrationBuilder.Sql("UPDATE QuotationItems SET ReconciliationStatus = 'LEGACY_UNMAPPED' WHERE MappedRequestLineItemId IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReconciliationJustification",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "ReconciliationStatus",
                table: "QuotationItems");
        }
    }
}
