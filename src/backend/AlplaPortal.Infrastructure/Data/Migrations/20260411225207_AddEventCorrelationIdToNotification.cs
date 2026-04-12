using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventCorrelationIdToNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "InformationalNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventCorrelationId",
                table: "InformationalNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InformationalNotifications_EventCorrelation_User",
                table: "InformationalNotifications",
                columns: new[] { "EventCorrelationId", "UserId" },
                filter: "[EventCorrelationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InformationalNotifications_EventCorrelation_User",
                table: "InformationalNotifications");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "InformationalNotifications");

            migrationBuilder.DropColumn(
                name: "EventCorrelationId",
                table: "InformationalNotifications");
        }
    }
}
