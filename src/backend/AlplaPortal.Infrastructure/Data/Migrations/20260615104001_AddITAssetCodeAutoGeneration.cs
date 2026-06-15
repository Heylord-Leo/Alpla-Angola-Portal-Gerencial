using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddITAssetCodeAutoGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "ITEquipmentTypes",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompanyCode",
                table: "ITEquipments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ITEquipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentTypeShortCode",
                table: "ITEquipments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyAssetCode",
                table: "ITEquipments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlantCode",
                table: "ITEquipments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlantId",
                table: "ITEquipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrl",
                table: "ITEquipments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceNumber",
                table: "ITEquipments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "APA");

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "APS");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "ShortCode",
                value: "NBK");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "ShortCode",
                value: "DSK");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000003"),
                column: "ShortCode",
                value: "MON");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000004"),
                column: "ShortCode",
                value: "PRN");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000005"),
                column: "ShortCode",
                value: "NVR");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000006"),
                column: "ShortCode",
                value: "MOU");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000007"),
                column: "ShortCode",
                value: "KBD");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000008"),
                column: "ShortCode",
                value: "HDS");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000009"),
                column: "ShortCode",
                value: "DOC");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000a"),
                column: "ShortCode",
                value: "BAG");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000b"),
                column: "ShortCode",
                value: "TEL");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000c"),
                column: "ShortCode",
                value: "CHG");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000d"),
                column: "ShortCode",
                value: "TAB");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000e"),
                column: "ShortCode",
                value: "SRV");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-00000000000f"),
                column: "ShortCode",
                value: "RTR");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000010"),
                column: "ShortCode",
                value: "AP");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000011"),
                column: "ShortCode",
                value: "SWT");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000012"),
                column: "ShortCode",
                value: "FWL");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000013"),
                column: "ShortCode",
                value: "UPS");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000014"),
                column: "ShortCode",
                value: "PRJ");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000015"),
                column: "ShortCode",
                value: "SCN");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000016"),
                column: "ShortCode",
                value: "ACC");

            migrationBuilder.UpdateData(
                table: "ITEquipmentTypes",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000017"),
                column: "ShortCode",
                value: "OTH");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "AOVIA1");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "AOVIA2");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 3,
                column: "Code",
                value: "AOVIA3");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentTypes_ShortCode",
                table: "ITEquipmentTypes",
                column: "ShortCode",
                unique: true,
                filter: "\"ShortCode\" IS NOT NULL AND \"ShortCode\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_CompanyId",
                table: "ITEquipments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_PlantId",
                table: "ITEquipments",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Code",
                table: "Companies",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipments_Companies_CompanyId",
                table: "ITEquipments",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipments_Plants_PlantId",
                table: "ITEquipments",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipments_Companies_CompanyId",
                table: "ITEquipments");

            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipments_Plants_PlantId",
                table: "ITEquipments");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentTypes_ShortCode",
                table: "ITEquipmentTypes");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipments_CompanyId",
                table: "ITEquipments");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipments_PlantId",
                table: "ITEquipments");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Code",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "ITEquipmentTypes");

            migrationBuilder.DropColumn(
                name: "CompanyCode",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "EquipmentTypeShortCode",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "LegacyAssetCode",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "PlantCode",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "PlantId",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "QrCodeUrl",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "ITEquipments");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Companies");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "V1");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "V2");

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 3,
                column: "Code",
                value: "V3");
        }
    }
}
