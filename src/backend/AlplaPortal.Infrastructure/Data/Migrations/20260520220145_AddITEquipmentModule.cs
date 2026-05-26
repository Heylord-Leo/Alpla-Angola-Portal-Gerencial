using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddITEquipmentModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ITEquipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Plant = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EquipmentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MacAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Processor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MemoryRam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BiometricMfaEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IdCard = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DevicePhotoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrentOwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentOwnerEmployeeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipments_Users_CurrentOwnerUserId",
                        column: x => x.CurrentOwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ITEquipments_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentAcquisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcquisitionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    PurchaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseRequestNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinancePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    WarrantyStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarrantyEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarrantyNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcquisitionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentAcquisitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentAcquisitions_ITEquipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "ITEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ITEquipmentAcquisitions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentAcquisitions_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssignedToDepartment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedToPlant = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpectedReturnDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignmentStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentAssignments_ITEquipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "ITEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ITEquipmentAssignments_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ITEquipmentAssignments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentMovementLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreviousOwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewOwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentMovementLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentMovementLogs_ITEquipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "ITEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ITEquipmentMovementLogs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ITEquipmentDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEquipmentDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDocuments_ITEquipmentAcquisitions_AcquisitionId",
                        column: x => x.AcquisitionId,
                        principalTable: "ITEquipmentAcquisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDocuments_ITEquipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "ITEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ITEquipmentDocuments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "RoleName" },
                values: new object[] { 13, "IT" });

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAcquisitions_CreatedByUserId",
                table: "ITEquipmentAcquisitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAcquisitions_EquipmentId",
                table: "ITEquipmentAcquisitions",
                column: "EquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAcquisitions_UpdatedByUserId",
                table: "ITEquipmentAcquisitions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAssignments_AssignedToUserId",
                table: "ITEquipmentAssignments",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAssignments_CreatedByUserId",
                table: "ITEquipmentAssignments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentAssignments_EquipmentId",
                table: "ITEquipmentAssignments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDocuments_AcquisitionId",
                table: "ITEquipmentDocuments",
                column: "AcquisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDocuments_EquipmentId",
                table: "ITEquipmentDocuments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentDocuments_UploadedByUserId",
                table: "ITEquipmentDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentMovementLogs_CreatedByUserId",
                table: "ITEquipmentMovementLogs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipmentMovementLogs_EquipmentId",
                table: "ITEquipmentMovementLogs",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_AssetTag",
                table: "ITEquipments",
                column: "AssetTag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_CreatedByUserId",
                table: "ITEquipments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_CurrentOwnerUserId",
                table: "ITEquipments",
                column: "CurrentOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_Hostname",
                table: "ITEquipments",
                column: "Hostname",
                unique: true,
                filter: "\"Hostname\" IS NOT NULL AND \"Hostname\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_MacAddress",
                table: "ITEquipments",
                column: "MacAddress",
                unique: true,
                filter: "\"MacAddress\" IS NOT NULL AND \"MacAddress\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_SerialNumber",
                table: "ITEquipments",
                column: "SerialNumber",
                unique: true,
                filter: "\"SerialNumber\" IS NOT NULL AND \"SerialNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_StatusCode",
                table: "ITEquipments",
                column: "StatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_ITEquipments_UpdatedByUserId",
                table: "ITEquipments",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ITEquipmentAssignments");

            migrationBuilder.DropTable(
                name: "ITEquipmentDocuments");

            migrationBuilder.DropTable(
                name: "ITEquipmentMovementLogs");

            migrationBuilder.DropTable(
                name: "ITEquipmentAcquisitions");

            migrationBuilder.DropTable(
                name: "ITEquipments");

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 13);
        }
    }
}
