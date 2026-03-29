using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingFieldsToItemsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DivergenceNotes",
                table: "RequestLineItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "RequestLineItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DivergenceNotes",
                table: "QuotationItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LineItemStatusId",
                table: "QuotationItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "QuotationItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_LineItemStatusId",
                table: "QuotationItems",
                column: "LineItemStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItems_LineItemStatuses_LineItemStatusId",
                table: "QuotationItems",
                column: "LineItemStatusId",
                principalTable: "LineItemStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_LineItemStatuses_LineItemStatusId",
                table: "QuotationItems");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_LineItemStatusId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "DivergenceNotes",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "DivergenceNotes",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "LineItemStatusId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "QuotationItems");
        }
    }
}
