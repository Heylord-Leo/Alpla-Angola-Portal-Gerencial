using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantIdToCostCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlantId",
                table: "CostCenters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "Name", "PlantId" },
                values: new object[] { "PET1", "PET 1", 1 });

            migrationBuilder.UpdateData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Code", "Name", "PlantId" },
                values: new object[] { "CAPS1", "CAPS 1", 1 });

            migrationBuilder.InsertData(
                table: "CostCenters",
                columns: new[] { "Id", "Code", "IsActive", "Name", "PlantId" },
                values: new object[,]
                {
                    { 3, "PET2", true, "PET 2", 2 },
                    { 4, "CAPS2", true, "CAPS 2", 2 },
                    { 5, "SBM", true, "SBM", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_PlantId",
                table: "CostCenters",
                column: "PlantId");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenters_Plants_PlantId",
                table: "CostCenters",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostCenters_Plants_PlantId",
                table: "CostCenters");

            migrationBuilder.DropIndex(
                name: "IX_CostCenters_PlantId",
                table: "CostCenters");

            migrationBuilder.DeleteData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "PlantId",
                table: "CostCenters");

            migrationBuilder.UpdateData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "Name" },
                values: new object[] { "CC-LOG-01", "CC-LOG-01" });

            migrationBuilder.UpdateData(
                table: "CostCenters",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Code", "Name" },
                values: new object[] { "CC-ADM-01", "CC-ADM-01" });
        }
    }
}
