using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedSubmittedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shift existing display orders to make room for SUBMITTED at DisplayOrder = 2
            migrationBuilder.Sql("UPDATE RequestStatuses SET DisplayOrder = DisplayOrder + 1 WHERE DisplayOrder >= 2");

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 19, "cyan", "SUBMITTED", 2, true, "Submetido" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.Sql("UPDATE RequestStatuses SET DisplayOrder = DisplayOrder - 1 WHERE DisplayOrder > 2");
        }
    }
}
