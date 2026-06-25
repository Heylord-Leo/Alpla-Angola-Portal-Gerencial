using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrModuleConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OcrModuleConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AllowedExtensions = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaxFileSizeMb = table.Column<int>(type: "int", nullable: true),
                    ProviderOverride = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModelOverride = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrModuleConfigs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "OcrModuleConfigs",
                columns: new[] { "Id", "AllowedExtensions", "DisplayName", "IsEnabled", "MaxFileSizeMb", "ModelOverride", "ModuleKey", "ProviderOverride", "UpdatedAtUtc", "UpdatedBy" },
                values: new object[,]
                {
                    { 1, ".pdf,.jpg,.jpeg,.png", "Requests & Buy2Pay", true, null, null, "REQUESTS", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System" },
                    { 2, ".pdf,.jpg,.jpeg,.png", "Contracts Management", true, null, null, "CONTRACTS", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcrModuleConfigs");
        }
    }
}
