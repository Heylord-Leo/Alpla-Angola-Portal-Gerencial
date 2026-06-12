using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddITEquipmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ITEquipmentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentTypes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ITEquipmentTypes",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayName", "IsActive", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "LAPTOP", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Laptop", true, 1, null },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "DESKTOP", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Desktop", true, 2, null },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "MONITOR", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Monitor", true, 3, null },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "PRINTER", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Impressora", true, 4, null },
                    { new Guid("a0000001-0000-0000-0000-000000000005"), "NVR", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "NVR", true, 5, null },
                    { new Guid("a0000001-0000-0000-0000-000000000006"), "MOUSE", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rato", true, 6, null },
                    { new Guid("a0000001-0000-0000-0000-000000000007"), "KEYBOARD", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Teclado", true, 7, null },
                    { new Guid("a0000001-0000-0000-0000-000000000008"), "HEADSET", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Headset", true, 8, null },
                    { new Guid("a0000001-0000-0000-0000-000000000009"), "DOCKING_STATION", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Docking Station", true, 9, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000a"), "BAG", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Mala / Bolsa", true, 10, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000b"), "PHONE", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Telemóvel", true, 11, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000c"), "CHARGER", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Carregador", true, 12, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000d"), "TABLET", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Tablet", true, 13, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000e"), "SERVER", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Servidor", true, 14, null },
                    { new Guid("a0000001-0000-0000-0000-00000000000f"), "NETWORK_EQUIPMENT", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Equipamento de Rede", true, 15, null },
                    { new Guid("a0000001-0000-0000-0000-000000000010"), "ACCESS_POINT", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Access Point", true, 16, null },
                    { new Guid("a0000001-0000-0000-0000-000000000011"), "SWITCH", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Switch", true, 17, null },
                    { new Guid("a0000001-0000-0000-0000-000000000012"), "FIREWALL", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Firewall", true, 18, null },
                    { new Guid("a0000001-0000-0000-0000-000000000013"), "UPS", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "UPS / Nobreak", true, 19, null },
                    { new Guid("a0000001-0000-0000-0000-000000000014"), "PROJECTOR", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Projetor", true, 20, null },
                    { new Guid("a0000001-0000-0000-0000-000000000015"), "SCANNER", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Scanner", true, 21, null },
                    { new Guid("a0000001-0000-0000-0000-000000000016"), "ACCESSORIES", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Acessórios", true, 22, null },
                    { new Guid("a0000001-0000-0000-0000-000000000017"), "UNKNOWN", new DateTime(2026, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Desconhecido", true, 99, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentTypes_Code",
                table: "ITEquipmentTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ITEquipmentTypes");
        }
    }
}
