using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ActionLabel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CcEmails = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    MaxRetries = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EventCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Correlation_Recipient_Active",
                table: "EmailOutbox",
                columns: new[] { "CorrelationId", "RecipientEmail" },
                unique: true,
                filter: "[CorrelationId] IS NOT NULL AND [Status] IN ('PENDING', 'PROCESSING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Processing_CreatedAt",
                table: "EmailOutbox",
                columns: new[] { "Status", "CreatedAtUtc" },
                filter: "[Status] = 'PROCESSING'");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_RequestId",
                table: "EmailOutbox",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_NextRetry",
                table: "EmailOutbox",
                columns: new[] { "Status", "NextRetryAtUtc" },
                filter: "[Status] IN ('PENDING', 'FAILED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOutbox");
        }
    }
}
