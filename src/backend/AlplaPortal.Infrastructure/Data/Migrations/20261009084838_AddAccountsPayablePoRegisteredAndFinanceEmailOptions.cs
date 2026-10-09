using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsPayablePoRegisteredAndFinanceEmailOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApNotifLogs_Dedup",
                table: "AccountsPayableNotificationLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "AccountsPayableNotificationLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyFinanceUsersByEmail",
                table: "AccountsPayableNotificationConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPoRegistered",
                table: "AccountsPayableNotificationConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ApNotifLogs_Dedup",
                table: "AccountsPayableNotificationLogs",
                columns: new[] { "RequestId", "EventCode", "RecipientEmail", "CorrelationId" },
                unique: true,
                filter: "[Success] = 1 AND [Skipped] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApNotifLogs_Dedup",
                table: "AccountsPayableNotificationLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AccountsPayableNotificationLogs");

            migrationBuilder.DropColumn(
                name: "NotifyFinanceUsersByEmail",
                table: "AccountsPayableNotificationConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyOnPoRegistered",
                table: "AccountsPayableNotificationConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_ApNotifLogs_Dedup",
                table: "AccountsPayableNotificationLogs",
                columns: new[] { "RequestId", "EventCode", "RecipientEmail" },
                unique: true,
                filter: "[Success] = 1 AND [Skipped] = 0");
        }
    }
}
