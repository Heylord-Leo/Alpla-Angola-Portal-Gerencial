using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProformaAlertOutboxLinkAndOutboxExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastQueuedAtUtc",
                table: "ProformaDeadlineAlerts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OutboxEntryId",
                table: "ProformaDeadlineAlerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueuedCount",
                table: "ProformaDeadlineAlerts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "EmailOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProformaDeadlineAlerts_OutboxEntryId",
                table: "ProformaDeadlineAlerts",
                column: "OutboxEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProformaDeadlineAlerts_EmailOutbox_OutboxEntryId",
                table: "ProformaDeadlineAlerts",
                column: "OutboxEntryId",
                principalTable: "EmailOutbox",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProformaDeadlineAlerts_EmailOutbox_OutboxEntryId",
                table: "ProformaDeadlineAlerts");

            migrationBuilder.DropIndex(
                name: "IX_ProformaDeadlineAlerts_OutboxEntryId",
                table: "ProformaDeadlineAlerts");

            migrationBuilder.DropColumn(
                name: "LastQueuedAtUtc",
                table: "ProformaDeadlineAlerts");

            migrationBuilder.DropColumn(
                name: "OutboxEntryId",
                table: "ProformaDeadlineAlerts");

            migrationBuilder.DropColumn(
                name: "QueuedCount",
                table: "ProformaDeadlineAlerts");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "EmailOutbox");
        }
    }
}
