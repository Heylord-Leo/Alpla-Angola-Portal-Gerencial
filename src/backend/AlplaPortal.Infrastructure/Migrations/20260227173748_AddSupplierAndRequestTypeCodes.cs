using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAndRequestTypeCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "RequestTypes",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "Requests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "RequestTypes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "Name" },
                values: new object[] { "QUOTATION", "Cotação" });

            migrationBuilder.UpdateData(
                table: "RequestTypes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Code", "Name" },
                values: new object[] { "PAYMENT", "Pagamento" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_Code",
                table: "RequestTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_SupplierId",
                table: "Requests",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Suppliers_SupplierId",
                table: "Requests",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Suppliers_SupplierId",
                table: "Requests");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_RequestTypes_Code",
                table: "RequestTypes");

            migrationBuilder.DropIndex(
                name: "IX_Requests_SupplierId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "RequestTypes");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Requests");

            migrationBuilder.UpdateData(
                table: "RequestTypes",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Purchase");

            migrationBuilder.UpdateData(
                table: "RequestTypes",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Payment");
        }
    }
}
