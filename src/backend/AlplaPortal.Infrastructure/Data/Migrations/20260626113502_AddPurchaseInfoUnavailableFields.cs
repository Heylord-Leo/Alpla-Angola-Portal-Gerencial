using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInfoUnavailableFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PurchaseInfoUnavailable",
                table: "ITEquipmentAcquisitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseInfoUnavailableReason",
                table: "ITEquipmentAcquisitions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseInfoUnavailable",
                table: "ITEquipmentAcquisitions");

            migrationBuilder.DropColumn(
                name: "PurchaseInfoUnavailableReason",
                table: "ITEquipmentAcquisitions");
        }
    }
}
