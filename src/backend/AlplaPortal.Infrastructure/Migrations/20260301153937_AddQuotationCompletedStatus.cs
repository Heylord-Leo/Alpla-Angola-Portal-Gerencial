using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationCompletedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Aguardando Aprovação da Área");

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 20, "emerald", "QUOTATION_COMPLETED", 20, true, "Cotação Concluída" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Aguardando Aprovação Área");
        }
    }
}
