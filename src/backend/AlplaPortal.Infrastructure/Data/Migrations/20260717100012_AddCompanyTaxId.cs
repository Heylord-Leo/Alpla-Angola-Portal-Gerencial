using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyTaxId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxId",
                table: "Companies",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 1,
                column: "TaxId",
                value: "5417567485");

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 2,
                column: "TaxId",
                value: "5001760246");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_TaxId",
                table: "Companies",
                column: "TaxId",
                unique: true,
                filter: "[TaxId] IS NOT NULL AND [TaxId] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_TaxId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "Companies");
        }
    }
}
