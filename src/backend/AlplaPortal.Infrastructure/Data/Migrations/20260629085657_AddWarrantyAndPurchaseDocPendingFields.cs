using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWarrantyAndPurchaseDocPendingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PurchaseDocumentPending",
                table: "ITEquipments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WarrantyInfoUnavailable",
                table: "ITEquipmentAcquisitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WarrantyInfoUnavailableReason",
                table: "ITEquipmentAcquisitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarrantyMonths",
                table: "ITEquipmentAcquisitions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseDocumentPending",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "WarrantyInfoUnavailable",
                table: "ITEquipmentAcquisitions");

            migrationBuilder.DropColumn(
                name: "WarrantyInfoUnavailableReason",
                table: "ITEquipmentAcquisitions");

            migrationBuilder.DropColumn(
                name: "WarrantyMonths",
                table: "ITEquipmentAcquisitions");
        }
    }
}
