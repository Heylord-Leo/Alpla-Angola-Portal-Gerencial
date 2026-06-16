using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ITEquipmentRefinements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ManufactureDate",
                table: "ITEquipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WifiMacAddress",
                table: "ITEquipments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "ShortCode",
                value: "LAP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManufactureDate",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "WifiMacAddress",
                table: "ITEquipments");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "ShortCode",
                value: "NBK");
        }
    }
}
