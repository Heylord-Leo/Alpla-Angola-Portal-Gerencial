using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialSnapshotAndPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DEC-110: Financial Snapshot — captured immutably at Final Approval
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedTotalAmount",
                table: "Requests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCurrencyCode",
                table: "Requests",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            // DEC-110: Actual Payment Capture — entered by Finance at MarkAsPaid
            migrationBuilder.AddColumn<decimal>(
                name: "ActualPaidAmount",
                table: "Requests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualPaidAtUtc",
                table: "Requests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApprovedTotalAmount", table: "Requests");
            migrationBuilder.DropColumn(name: "ApprovedCurrencyCode", table: "Requests");
            migrationBuilder.DropColumn(name: "ApprovedAtUtc", table: "Requests");
            migrationBuilder.DropColumn(name: "ActualPaidAmount", table: "Requests");
            migrationBuilder.DropColumn(name: "ActualPaidAtUtc", table: "Requests");
        }
    }
}
