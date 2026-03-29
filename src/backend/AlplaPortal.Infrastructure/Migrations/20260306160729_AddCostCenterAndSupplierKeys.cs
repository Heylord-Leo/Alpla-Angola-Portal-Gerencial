using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCostCenterAndSupplierKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostCenterId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentTypeCode",
                table: "RequestAttachments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 20,
                column: "IsActive",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_CostCenterId",
                table: "RequestLineItems",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_SupplierId",
                table: "RequestLineItems",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_Code",
                table: "CostCenters",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_Suppliers_SupplierId",
                table: "RequestLineItems",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_CostCenters_CostCenterId",
                table: "RequestLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_Suppliers_SupplierId",
                table: "RequestLineItems");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_CostCenterId",
                table: "RequestLineItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_SupplierId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "AttachmentTypeCode",
                table: "RequestAttachments");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 20,
                column: "IsActive",
                value: true);
        }
    }
}
