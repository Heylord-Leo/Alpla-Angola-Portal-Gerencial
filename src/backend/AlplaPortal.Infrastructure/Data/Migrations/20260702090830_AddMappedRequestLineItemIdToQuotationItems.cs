using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMappedRequestLineItemIdToQuotationItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MappedRequestLineItemId",
                table: "QuotationItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_MappedRequestLineItemId",
                table: "QuotationItems",
                column: "MappedRequestLineItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItems_RequestLineItems_MappedRequestLineItemId",
                table: "QuotationItems",
                column: "MappedRequestLineItemId",
                principalTable: "RequestLineItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItems_RequestLineItems_MappedRequestLineItemId",
                table: "QuotationItems");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItems_MappedRequestLineItemId",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "MappedRequestLineItemId",
                table: "QuotationItems");
        }
    }
}
