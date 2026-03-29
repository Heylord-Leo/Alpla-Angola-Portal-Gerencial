using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitIdToQuotationItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "QuotationItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_UnitId",
                table: "QuotationItems",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItems_Units_UnitId",
                table: "QuotationItems",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");

            // Safe Backfill: Sync existing QuotationItems with their RequestLineItem's UnitId
            migrationBuilder.Sql(@"
                UPDATE qi
                SET qi.UnitId = li.UnitId
                FROM QuotationItems qi
                JOIN Quotations q ON qi.QuotationId = q.Id
                JOIN RequestLineItems li ON q.RequestId = li.RequestId AND qi.LineNumber = li.LineNumber
                WHERE qi.UnitId IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_Units_UnitId",
                table: "QuotationItems");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_UnitId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "QuotationItems");
        }
    }
}
