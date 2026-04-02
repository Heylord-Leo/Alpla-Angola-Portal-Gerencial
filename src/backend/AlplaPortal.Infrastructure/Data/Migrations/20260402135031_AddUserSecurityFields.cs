using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                column: "BadgeColor",
                value: "sky");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 12,
                column: "BadgeColor",
                value: "lime");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                column: "BadgeColor",
                value: "carbon");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                column: "BadgeColor",
                value: "zinc");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 21,
                column: "BadgeColor",
                value: "amber");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "AccessFailedCount", "LockoutEndUtc" },
                values: new object[] { 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "AccessFailedCount", "LockoutEndUtc" },
                values: new object[] { 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "AccessFailedCount", "LockoutEndUtc" },
                values: new object[] { 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "AccessFailedCount", "LockoutEndUtc" },
                values: new object[] { 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "AccessFailedCount", "LockoutEndUtc" },
                values: new object[] { 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "Users");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                column: "BadgeColor",
                value: "cyan");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 12,
                column: "BadgeColor",
                value: "sky");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                column: "BadgeColor",
                value: "emerald");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                column: "BadgeColor",
                value: "amber");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 21,
                column: "BadgeColor",
                value: "indigo");
        }
    }
}
