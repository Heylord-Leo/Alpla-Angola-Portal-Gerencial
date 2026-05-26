using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalBudgetScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnnualBudgets_Year_DepartmentId_CurrencyId",
                table: "AnnualBudgets");

            migrationBuilder.Sql("DELETE FROM AnnualBudgets;");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "AnnualBudgets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CostCenterId",
                table: "AnnualBudgets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AnnualBudgets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PlantId",
                table: "AnnualBudgets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AnnualBudget_Hierarchy",
                table: "AnnualBudgets",
                columns: new[] { "Year", "CompanyId", "PlantId", "DepartmentId", "CostCenterId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnualBudgets_CompanyId",
                table: "AnnualBudgets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnualBudgets_CostCenterId",
                table: "AnnualBudgets",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnualBudgets_PlantId",
                table: "AnnualBudgets",
                column: "PlantId");

            migrationBuilder.AddForeignKey(
                name: "FK_AnnualBudgets_Companies_CompanyId",
                table: "AnnualBudgets",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AnnualBudgets_CostCenters_CostCenterId",
                table: "AnnualBudgets",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AnnualBudgets_Plants_PlantId",
                table: "AnnualBudgets",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnnualBudgets_Companies_CompanyId",
                table: "AnnualBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_AnnualBudgets_CostCenters_CostCenterId",
                table: "AnnualBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_AnnualBudgets_Plants_PlantId",
                table: "AnnualBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AnnualBudget_Hierarchy",
                table: "AnnualBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AnnualBudgets_CompanyId",
                table: "AnnualBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AnnualBudgets_CostCenterId",
                table: "AnnualBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AnnualBudgets_PlantId",
                table: "AnnualBudgets");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AnnualBudgets");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "AnnualBudgets");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AnnualBudgets");

            migrationBuilder.DropColumn(
                name: "PlantId",
                table: "AnnualBudgets");

            migrationBuilder.CreateIndex(
                name: "IX_AnnualBudgets_Year_DepartmentId_CurrencyId",
                table: "AnnualBudgets",
                columns: new[] { "Year", "DepartmentId", "CurrencyId" },
                unique: true);
        }
    }
}
