using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedReturnDocumentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "SignedReturnDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAcquisitions_SupplierId",
                table: "ITEquipmentAcquisitions",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentAcquisitions_Suppliers_SupplierId",
                table: "ITEquipmentAcquisitions",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "SignedReturnDocumentId",
                principalTable: "ITEquipmentDocuments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentAcquisitions_Suppliers_SupplierId",
                table: "ITEquipmentAcquisitions");

            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDeliveryTerms_SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentAcquisitions_SupplierId",
                table: "ITEquipmentAcquisitions");

            migrationBuilder.DropColumn(
                name: "SignedReturnDocumentId",
                table: "ITEquipmentDeliveryTerms");
        }
    }
}
