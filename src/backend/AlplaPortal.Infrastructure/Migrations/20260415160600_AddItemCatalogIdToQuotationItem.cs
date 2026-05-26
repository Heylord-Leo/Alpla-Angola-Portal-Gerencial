using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCatalogIdToQuotationItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemCatalogId",
                table: "QuotationItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_ItemCatalogId",
                table: "QuotationItems",
                column: "ItemCatalogId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItems_ItemCatalogItems_ItemCatalogId",
                table: "QuotationItems",
                column: "ItemCatalogId",
                principalTable: "ItemCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_ItemCatalogItems_ItemCatalogId",
                table: "QuotationItems");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_ItemCatalogId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "ItemCatalogId",
                table: "QuotationItems");
        }
    }
}
