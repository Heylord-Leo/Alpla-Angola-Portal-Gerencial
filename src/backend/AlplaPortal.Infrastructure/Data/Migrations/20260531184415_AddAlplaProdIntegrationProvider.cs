using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlplaProdIntegrationProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "IntegrationProviders",
                columns: new[] { "Id", "Capabilities", "Code", "ConnectionType", "CreatedAtUtc", "Description", "DisplayOrder", "Environment", "IsEnabled", "IsPlanned", "Name", "ProviderType", "UpdatedAtUtc" },
                values: new object[] { 5, "[\"OPERATIONS\",\"TRANSFERS\",\"LOGISTICS\"]", "ALPLAPROD", "SQL", new DateTime(2026, 5, 31, 0, 0, 0, 0, DateTimeKind.Utc), "AlplaPROD 1.0 production databases — Viana 1, Viana 2, Viana 3", 40, "PRODUCTION", false, true, "AlplaPROD 1.0 (Production)", "PRODUCTION", null });

            migrationBuilder.InsertData(
                table: "IntegrationConnectionStatuses",
                columns: new[] { "Id", "ConsecutiveFailures", "CurrentStatus", "IntegrationProviderId", "LastCheckedAtUtc", "LastErrorMessage", "LastFailureUtc", "LastResponseTimeMs", "LastSuccessUtc", "LastTestedByEmail" },
                values: new object[] { 5, 0, "PLANNED", 5, null, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "IntegrationConnectionStatuses",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 5);
        }
    }
}
