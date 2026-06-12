using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddITEquipmentCatalogLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ITEquipmentDeliveryTerms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmployeePlantId",
                table: "ITEquipmentDeliveryTerms",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ITEquipmentManufacturers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentManufacturers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentMemoryOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ValueInGb = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentMemoryOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentProcessors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentProcessors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManufacturerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentModels_ITEquipmentManufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "ITEquipmentManufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ITEquipmentManufacturers",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("b0000001-0000-0000-0000-000000000001"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "HP", 1, null },
                    { new Guid("b0000001-0000-0000-0000-000000000002"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dell", 2, null },
                    { new Guid("b0000001-0000-0000-0000-000000000003"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Lenovo", 3, null },
                    { new Guid("b0000001-0000-0000-0000-000000000004"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Apple", 4, null },
                    { new Guid("b0000001-0000-0000-0000-000000000005"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Microsoft", 5, null },
                    { new Guid("b0000001-0000-0000-0000-000000000006"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Samsung", 6, null },
                    { new Guid("b0000001-0000-0000-0000-000000000007"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "LG", 7, null },
                    { new Guid("b0000001-0000-0000-0000-000000000008"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Brother", 8, null },
                    { new Guid("b0000001-0000-0000-0000-000000000009"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Canon", 9, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000a"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Epson", 10, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000b"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Hikvision", 11, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000c"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dahua", 12, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000d"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Cisco", 13, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000e"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Fortinet", 14, null },
                    { new Guid("b0000001-0000-0000-0000-00000000000f"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Ubiquiti", 15, null },
                    { new Guid("b0000001-0000-0000-0000-000000000010"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "APC", 16, null },
                    { new Guid("b0000001-0000-0000-0000-000000000011"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Logitech", 17, null },
                    { new Guid("b0000001-0000-0000-0000-000000000012"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "N/A", 99, null }
                });

            migrationBuilder.InsertData(
                table: "ITEquipmentMemoryOptions",
                columns: new[] { "Id", "CreatedAt", "DisplayName", "IsActive", "SortOrder", "UpdatedAt", "ValueInGb" },
                values: new object[,]
                {
                    { new Guid("d0000001-0000-0000-0000-000000000001"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "4 GB", true, 1, null, 4 },
                    { new Guid("d0000001-0000-0000-0000-000000000002"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "8 GB", true, 2, null, 8 },
                    { new Guid("d0000001-0000-0000-0000-000000000003"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "16 GB", true, 3, null, 16 },
                    { new Guid("d0000001-0000-0000-0000-000000000004"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "32 GB", true, 4, null, 32 },
                    { new Guid("d0000001-0000-0000-0000-000000000005"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "64 GB", true, 5, null, 64 },
                    { new Guid("d0000001-0000-0000-0000-000000000006"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "128 GB", true, 6, null, 128 },
                    { new Guid("d0000001-0000-0000-0000-000000000007"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "N/A", true, 99, null, null }
                });

            migrationBuilder.InsertData(
                table: "ITEquipmentProcessors",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("c0000001-0000-0000-0000-000000000001"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Intel Core i3", 1, null },
                    { new Guid("c0000001-0000-0000-0000-000000000002"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Intel Core i5", 2, null },
                    { new Guid("c0000001-0000-0000-0000-000000000003"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Intel Core i7", 3, null },
                    { new Guid("c0000001-0000-0000-0000-000000000004"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Intel Core i9", 4, null },
                    { new Guid("c0000001-0000-0000-0000-000000000005"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Intel Xeon", 5, null },
                    { new Guid("c0000001-0000-0000-0000-000000000006"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "AMD Ryzen 3", 6, null },
                    { new Guid("c0000001-0000-0000-0000-000000000007"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "AMD Ryzen 5", 7, null },
                    { new Guid("c0000001-0000-0000-0000-000000000008"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "AMD Ryzen 7", 8, null },
                    { new Guid("c0000001-0000-0000-0000-000000000009"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "AMD Ryzen 9", 9, null },
                    { new Guid("c0000001-0000-0000-0000-00000000000a"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Apple M1", 10, null },
                    { new Guid("c0000001-0000-0000-0000-00000000000b"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Apple M2", 11, null },
                    { new Guid("c0000001-0000-0000-0000-00000000000c"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "Apple M3", 12, null },
                    { new Guid("c0000001-0000-0000-0000-00000000000d"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), true, "N/A", 99, null }
                });

            migrationBuilder.InsertData(
                table: "ITEquipmentModels",
                columns: new[] { "Id", "CreatedAt", "EquipmentTypeCode", "IsActive", "ManufacturerId", "Name", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("e0000001-0000-0000-0000-000000000001"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "LAPTOP", true, new Guid("b0000001-0000-0000-0000-000000000001"), "ProBook 440 G10", 1, null },
                    { new Guid("e0000001-0000-0000-0000-000000000002"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "LAPTOP", true, new Guid("b0000001-0000-0000-0000-000000000001"), "ProBook 450 G10", 2, null },
                    { new Guid("e0000001-0000-0000-0000-000000000003"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "MONITOR", true, new Guid("b0000001-0000-0000-0000-000000000001"), "E24 G5", 3, null },
                    { new Guid("e0000001-0000-0000-0000-000000000004"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "LAPTOP", true, new Guid("b0000001-0000-0000-0000-000000000002"), "Latitude 5440", 1, null },
                    { new Guid("e0000001-0000-0000-0000-000000000005"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "DESKTOP", true, new Guid("b0000001-0000-0000-0000-000000000002"), "OptiPlex 7010", 2, null },
                    { new Guid("e0000001-0000-0000-0000-000000000006"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "MONITOR", true, new Guid("b0000001-0000-0000-0000-000000000002"), "P2422H", 3, null },
                    { new Guid("e0000001-0000-0000-0000-000000000007"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "KEYBOARD", true, new Guid("b0000001-0000-0000-0000-000000000002"), "KB216", 4, null },
                    { new Guid("e0000001-0000-0000-0000-000000000008"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "LAPTOP", true, new Guid("b0000001-0000-0000-0000-000000000003"), "ThinkPad E14", 1, null },
                    { new Guid("e0000001-0000-0000-0000-000000000009"), new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "MOUSE", true, new Guid("b0000001-0000-0000-0000-000000000011"), "M90", 1, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_CompanyId",
                table: "ITEquipmentDeliveryTerms",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeeDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeePlantId",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeePlantId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentManufacturers_Name",
                table: "ITEquipmentManufacturers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentMemoryOptions_DisplayName",
                table: "ITEquipmentMemoryOptions",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentModels_ManufacturerId_Name",
                table: "ITEquipmentModels",
                columns: new[] { "ManufacturerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentProcessors_Name",
                table: "ITEquipmentProcessors",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Companies_CompanyId",
                table: "ITEquipmentDeliveryTerms",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Departments_EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeeDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Plants_EmployeePlantId",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeePlantId",
                principalTable: "Plants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Companies_CompanyId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Departments_EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_Plants_EmployeePlantId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropTable(
                name: "ITEquipmentMemoryOptions");

            migrationBuilder.DropTable(
                name: "ITEquipmentModels");

            migrationBuilder.DropTable(
                name: "ITEquipmentProcessors");

            migrationBuilder.DropTable(
                name: "ITEquipmentManufacturers");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDeliveryTerms_CompanyId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeePlantId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropColumn(
                name: "EmployeeDepartmentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropColumn(
                name: "EmployeePlantId",
                table: "ITEquipmentDeliveryTerms");
        }
    }
}
