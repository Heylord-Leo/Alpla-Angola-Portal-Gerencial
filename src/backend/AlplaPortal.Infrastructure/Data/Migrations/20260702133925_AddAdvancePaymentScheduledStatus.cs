using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancePaymentScheduledStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 27, "orange", "PO_PARTIALLY_UPLOADED", 27, true, "P.O Parcialmente Registrada" },
                    { 28, "#17a2b8", "ADVANCE_PAYMENT_SCHEDULED", 28, true, "Adiantamento Agendado" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 28);
        }
    }
}
