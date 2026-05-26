using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "IntegrationProviderSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SecretVersion",
                table: "IntegrationProviderSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "IntegrationProviderSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                table: "IntegrationProviders",
                columns: new[] { "Id", "Capabilities", "Code", "ConnectionType", "CreatedAtUtc", "Description", "DisplayOrder", "Environment", "IsEnabled", "IsPlanned", "Name", "ProviderType", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 3, "[\"DOCUMENT_EXTRACTION\",\"OCR\"]", "OPENAI", "REST_API", new DateTime(2026, 5, 25, 0, 0, 0, 0, DateTimeKind.Utc), "AI-powered document extraction and analysis — OCR processing for proformas, invoices, and contracts.", 3, "PRODUCTION", true, false, "OpenAI / ChatGPT API", "API", null },
                    { 4, "[\"EMAIL_NOTIFICATIONS\",\"ALERTS\"]", "SMTP", "SMTP", new DateTime(2026, 5, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Email notification service — sends workflow alerts, password resets, and proforma deadline reminders.", 4, "PRODUCTION", true, false, "Email / SMTP Service", "API", null }
                });

            migrationBuilder.InsertData(
                table: "IntegrationConnectionStatuses",
                columns: new[] { "Id", "ConsecutiveFailures", "CurrentStatus", "IntegrationProviderId", "LastCheckedAtUtc", "LastErrorMessage", "LastFailureUtc", "LastResponseTimeMs", "LastSuccessUtc", "LastTestedByEmail" },
                values: new object[,]
                {
                    { 3, 0, "NOT_CONFIGURED", 3, null, null, null, null, null, null },
                    { 4, 0, "NOT_CONFIGURED", 4, null, null, null, null, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "IntegrationConnectionStatuses",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "IntegrationConnectionStatuses",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "IntegrationProviderSettings");

            migrationBuilder.DropColumn(
                name: "SecretVersion",
                table: "IntegrationProviderSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "IntegrationProviderSettings");
        }
    }
}
