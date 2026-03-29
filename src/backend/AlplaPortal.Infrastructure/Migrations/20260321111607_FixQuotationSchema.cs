using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixQuotationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProformaAttachmentId",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_ProformaAttachmentId",
                table: "Quotations",
                column: "ProformaAttachmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_RequestAttachments_ProformaAttachmentId",
                table: "Quotations",
                column: "ProformaAttachmentId",
                principalTable: "RequestAttachments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_RequestAttachments_ProformaAttachmentId",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_ProformaAttachmentId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ProformaAttachmentId",
                table: "Quotations");
        }
    }
}
