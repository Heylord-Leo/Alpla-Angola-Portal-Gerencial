using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelaxLineItemOptionalFieldsForDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_IvaRates_IvaRateId",
                table: "RequestLineItems");

            migrationBuilder.AlterColumn<int>(
                name: "IvaRateId",
                table: "RequestLineItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CostCenterId",
                table: "RequestLineItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_IvaRates_IvaRateId",
                table: "RequestLineItems",
                column: "IvaRateId",
                principalTable: "IvaRates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_IvaRates_IvaRateId",
                table: "RequestLineItems");

            migrationBuilder.AlterColumn<int>(
                name: "IvaRateId",
                table: "RequestLineItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CostCenterId",
                table: "RequestLineItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_IvaRates_IvaRateId",
                table: "RequestLineItems",
                column: "IvaRateId",
                principalTable: "IvaRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
