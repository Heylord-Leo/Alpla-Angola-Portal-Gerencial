using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSupplierColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Suppliers",
                newName: "PortalCode");

            migrationBuilder.AddColumn<string>(
                name: "PrimaveraCode",
                table: "Suppliers",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_PortalCode",
                table: "Suppliers",
                column: "PortalCode",
                unique: true,
                filter: "[PortalCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_PrimaveraCode",
                table: "Suppliers",
                column: "PrimaveraCode",
                unique: true,
                filter: "[PrimaveraCode] IS NOT NULL AND [PrimaveraCode] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_PrimaveraCode",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_PortalCode",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PrimaveraCode",
                table: "Suppliers");

            migrationBuilder.RenameColumn(
                name: "PortalCode",
                table: "Suppliers",
                newName: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }
    }
}
