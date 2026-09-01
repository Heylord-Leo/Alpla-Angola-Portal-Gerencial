using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationRevisionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RevisesQuotationId",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_RevisesQuotationId",
                table: "Quotations",
                column: "RevisesQuotationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_Quotations_RevisesQuotationId",
                table: "Quotations",
                column: "RevisesQuotationId",
                principalTable: "Quotations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_Quotations_RevisesQuotationId",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_RevisesQuotationId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RevisesQuotationId",
                table: "Quotations");
        }
    }
}
