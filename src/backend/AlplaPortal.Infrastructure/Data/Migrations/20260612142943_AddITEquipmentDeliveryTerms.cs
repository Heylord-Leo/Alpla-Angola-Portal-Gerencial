using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddITEquipmentDeliveryTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard: drop column if it already exists from a previous failed migration attempt
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'ITEquipmentDocuments') AND name = 'DeliveryTermId'
                )
                BEGIN
                    -- Also drop FK and index if they exist
                    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ITEquipmentDocuments_ITEquipmentDeliveryTerms_DeliveryTermId')
                        ALTER TABLE [ITEquipmentDocuments] DROP CONSTRAINT [FK_ITEquipmentDocuments_ITEquipmentDeliveryTerms_DeliveryTermId];
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ITEquipmentDocuments_DeliveryTermId' AND object_id = OBJECT_ID(N'ITEquipmentDocuments'))
                        DROP INDEX [IX_ITEquipmentDocuments_DeliveryTermId] ON [ITEquipmentDocuments];
                    ALTER TABLE [ITEquipmentDocuments] DROP COLUMN [DeliveryTermId];
                END
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTermId",
                table: "ITEquipmentDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ITEquipmentDeliveryTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeEmail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EmployeeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeDepartment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmployeePosition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmployeePlant = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GeneratedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SignedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentDeliveryTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_GeneratedDocumentId",
                        column: x => x.GeneratedDocumentId,
                        principalTable: "ITEquipmentDocuments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryTerms_ITEquipmentDocuments_SignedDocumentId",
                        column: x => x.SignedDocumentId,
                        principalTable: "ITEquipmentDocuments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryTerms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryTerms_Users_EmployeeUserId",
                        column: x => x.EmployeeUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryTerms_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentDeliveryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnCondition = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentDeliveryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryItems_ITEquipmentAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "ITEquipmentAssignments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryItems_ITEquipmentDeliveryTerms_DeliveryTermId",
                        column: x => x.DeliveryTermId,
                        principalTable: "ITEquipmentDeliveryTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDeliveryItems_ITEquipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "ITEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDocuments_DeliveryTermId",
                table: "ITEquipmentDocuments",
                column: "DeliveryTermId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryItems_AssignmentId",
                table: "ITEquipmentDeliveryItems",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryItems_DeliveryTermId",
                table: "ITEquipmentDeliveryItems",
                column: "DeliveryTermId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryItems_EquipmentId",
                table: "ITEquipmentDeliveryItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryItems_ItemStatus",
                table: "ITEquipmentDeliveryItems",
                column: "ItemStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_CreatedByUserId",
                table: "ITEquipmentDeliveryTerms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeeEmail",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeeEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_EmployeeUserId",
                table: "ITEquipmentDeliveryTerms",
                column: "EmployeeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_GeneratedDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "GeneratedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_SignedDocumentId",
                table: "ITEquipmentDeliveryTerms",
                column: "SignedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_Status",
                table: "ITEquipmentDeliveryTerms",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_TermNumber",
                table: "ITEquipmentDeliveryTerms",
                column: "TermNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDeliveryTerms_UpdatedByUserId",
                table: "ITEquipmentDeliveryTerms",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ITEquipmentDocuments_ITEquipmentDeliveryTerms_DeliveryTermId",
                table: "ITEquipmentDocuments",
                column: "DeliveryTermId",
                principalTable: "ITEquipmentDeliveryTerms",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ITEquipmentDocuments_ITEquipmentDeliveryTerms_DeliveryTermId",
                table: "ITEquipmentDocuments");

            migrationBuilder.DropTable(
                name: "ITEquipmentDeliveryItems");

            migrationBuilder.DropTable(
                name: "ITEquipmentDeliveryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ITEquipmentDocuments_DeliveryTermId",
                table: "ITEquipmentDocuments");

            migrationBuilder.DropColumn(
                name: "DeliveryTermId",
                table: "ITEquipmentDocuments");
        }
    }
}
