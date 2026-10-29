using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPoDocumentSupersessionAndExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentPoAttachmentId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "RequestAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByAttachmentId",
                table: "RequestAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByUserId",
                table: "RequestAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PoDocumentExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ExtractedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ProviderStatusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Verdict = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Family = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClassificationType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ClassificationConfidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    ReferencedDocumentNumbersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SupplierTaxId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoDocumentExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoDocumentExtractions_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoDocumentExtractions_Request_Hash_Created",
                table: "PoDocumentExtractions",
                columns: new[] { "RequestId", "FileHash", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoDocumentExtractions");

            migrationBuilder.DropColumn(
                name: "CurrentPoAttachmentId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "SupersededByAttachmentId",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "SupersededByUserId",
                table: "RequestAttachments");
        }
    }
}
