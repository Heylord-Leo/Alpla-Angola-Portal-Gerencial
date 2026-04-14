using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivateInnuxProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "IsPlanned", "ProviderType" },
                values: new object[] { false, "TIME_ATTENDANCE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "IntegrationProviders",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "IsPlanned", "ProviderType" },
                values: new object[] { true, "BIOMETRIC" });
        }
    }
}
