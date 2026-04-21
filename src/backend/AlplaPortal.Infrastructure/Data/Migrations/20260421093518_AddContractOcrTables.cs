using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractOcrTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OcrStatus",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OcrExtractionRecordId",
                table: "ContractDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractOcrExtractionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggeredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoutingStrategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    TotalTokensUsed = table.Column<int>(type: "int", nullable: false),
                    QualityScore = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    IsPartial = table.Column<bool>(type: "bit", nullable: false),
                    ConflictsDetected = table.Column<bool>(type: "bit", nullable: false),
                    NativeTextDetected = table.Column<bool>(type: "bit", nullable: false),
                    RawJsonResult = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractOcrExtractionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractOcrExtractionRecords_ContractDocuments_ContractDocumentId",
                        column: x => x.ContractDocumentId,
                        principalTable: "ContractDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractOcrExtractionRecords_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractOcrExtractionRecords_Users_TriggeredByUserId",
                        column: x => x.TriggeredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractOcrExtractedFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtractionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RawExtractedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalisedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    DisplayHint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfirmedByUser = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasOverridden = table.Column<bool>(type: "bit", nullable: false),
                    FinalSavedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscardedByUser = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractOcrExtractedFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractOcrExtractedFields_ContractOcrExtractionRecords_ExtractionRecordId",
                        column: x => x.ExtractionRecordId,
                        principalTable: "ContractOcrExtractionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocuments_OcrExtractionRecordId",
                table: "ContractDocuments",
                column: "OcrExtractionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractOcrExtractedFields_ContractId_FieldName",
                table: "ContractOcrExtractedFields",
                columns: new[] { "ContractId", "FieldName" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractOcrExtractedFields_ExtractionRecordId",
                table: "ContractOcrExtractedFields",
                column: "ExtractionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractOcrExtractionRecords_ContractDocumentId",
                table: "ContractOcrExtractionRecords",
                column: "ContractDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractOcrExtractionRecords_ContractId_Status",
                table: "ContractOcrExtractionRecords",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractOcrExtractionRecords_TriggeredByUserId",
                table: "ContractOcrExtractionRecords",
                column: "TriggeredByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocuments_ContractOcrExtractionRecords_OcrExtractionRecordId",
                table: "ContractDocuments",
                column: "OcrExtractionRecordId",
                principalTable: "ContractOcrExtractionRecords",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractDocuments_ContractOcrExtractionRecords_OcrExtractionRecordId",
                table: "ContractDocuments");

            migrationBuilder.DropTable(
                name: "ContractOcrExtractedFields");

            migrationBuilder.DropTable(
                name: "ContractOcrExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocuments_OcrExtractionRecordId",
                table: "ContractDocuments");

            migrationBuilder.DropColumn(
                name: "OcrStatus",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "OcrExtractionRecordId",
                table: "ContractDocuments");
        }
    }
}
