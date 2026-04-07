using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingPoCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 22, "red", "WAITING_PO_CORRECTION", 22, true, "Devolvido para Compras" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 22);
        }
    }
}
