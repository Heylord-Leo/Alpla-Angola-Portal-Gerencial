using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivatePrimaveraProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "IntegrationConnectionStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CurrentStatus",
                value: "NOT_CONFIGURED");

            migrationBuilder.UpdateData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsPlanned",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "IntegrationConnectionStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CurrentStatus",
                value: "PLANNED");

            migrationBuilder.UpdateData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsPlanned",
                value: true);
        }
    }
}
