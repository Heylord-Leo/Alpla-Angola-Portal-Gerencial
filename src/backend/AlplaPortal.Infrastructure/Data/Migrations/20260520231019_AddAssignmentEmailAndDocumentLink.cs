using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentEmailAndDocumentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignmentId",
                table: "ITEquipmentDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToEmail",
                table: "ITEquipmentAssignments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDocuments_AssignmentId",
                table: "ITEquipmentDocuments",
                column: "AssignmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDocuments_ITEquipmentAssignments_AssignmentId",
                table: "ITEquipmentDocuments",
                column: "AssignmentId",
                principalTable: "ITEquipmentAssignments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDocuments_ITEquipmentAssignments_AssignmentId",
                table: "ITEquipmentDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDocuments_AssignmentId",
                table: "ITEquipmentDocuments");

            migrationBuilder.DropColumn(
                name: "AssignmentId",
                table: "ITEquipmentDocuments");

            migrationBuilder.DropColumn(
                name: "AssignedToEmail",
                table: "ITEquipmentAssignments");
        }
    }
}
