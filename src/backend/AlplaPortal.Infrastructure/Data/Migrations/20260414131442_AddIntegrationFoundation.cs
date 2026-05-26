using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConnectionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsPlanned = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Capabilities = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationConnectionStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationProviderId = table.Column<int>(type: "int", nullable: false),
                    CurrentStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailureUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastResponseTimeMs = table.Column<int>(type: "int", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    LastTestedByEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnectionStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationConnectionStatuses_IntegrationProviders_IntegrationProviderId",
                        column: x => x.IntegrationProviderId,
                        principalTable: "IntegrationProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationProviderId = table.Column<int>(type: "int", nullable: false),
                    Server = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InstanceName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthenticationMode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiKeyEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    AdditionalConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationProviderSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationProviderSettings_IntegrationProviders_IntegrationProviderId",
                        column: x => x.IntegrationProviderId,
                        principalTable: "IntegrationProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "IntegrationProviders",
                columns: new[] { "Id", "Capabilities", "Code", "ConnectionType", "CreatedAtUtc", "Description", "DisplayOrder", "Environment", "IsEnabled", "IsPlanned", "Name", "ProviderType", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, "[\"EMPLOYEES\",\"MATERIALS\",\"SUPPLIERS\",\"DEPARTMENTS\",\"COST_CENTERS\"]", "PRIMAVERA", "SQL", new DateTime(2026, 4, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Enterprise Resource Planning — master data source for employees, articles, suppliers, departments, and cost centers.", 1, "PRODUCTION", false, true, "Primavera ERP", "ERP", null },
                    { 2, "[\"EMPLOYEES\",\"ATTENDANCE\"]", "INNUX", "SQL", new DateTime(2026, 4, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Biometric time and attendance system — complementary employee/attendance data source.", 2, "PRODUCTION", false, true, "Innux Time & Attendance", "BIOMETRIC", null }
                });

            migrationBuilder.InsertData(
                table: "IntegrationConnectionStatuses",
                columns: new[] { "Id", "ConsecutiveFailures", "CurrentStatus", "IntegrationProviderId", "LastCheckedAtUtc", "LastErrorMessage", "LastFailureUtc", "LastResponseTimeMs", "LastSuccessUtc", "LastTestedByEmail" },
                values: new object[,]
                {
                    { 1, 0, "PLANNED", 1, null, null, null, null, null, null },
                    { 2, 0, "PLANNED", 2, null, null, null, null, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnectionStatuses_IntegrationProviderId",
                table: "IntegrationConnectionStatuses",
                column: "IntegrationProviderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationProviders_Code",
                table: "IntegrationProviders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationProviderSettings_IntegrationProviderId",
                table: "IntegrationProviderSettings",
                column: "IntegrationProviderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationConnectionStatuses");

            migrationBuilder.DropTable(
                name: "IntegrationProviderSettings");

            migrationBuilder.DropTable(
                name: "IntegrationProviders");
        }
    }
}
