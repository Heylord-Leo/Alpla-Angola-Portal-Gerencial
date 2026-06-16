using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnDocumentToDeliveryTerm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "ReturnDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "ReturnDocumentId",
                principalTable: "ITEquipmentDocuments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDeliveryTerms_ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropColumn(
                name: "ReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");
        }
    }
}
