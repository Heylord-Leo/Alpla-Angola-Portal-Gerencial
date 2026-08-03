using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentClassificationOverrideAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentClassificationOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Context = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SuggestedType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    TitleFound = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConflictingEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SuggestionSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SelectedType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Acknowledged = table.Column<bool>(type: "bit", nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentClassificationOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentClassificationOverrides_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentClassificationOverrides_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentClassificationOverrides_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClassificationOverride_QuotationId",
                table: "DocumentClassificationOverrides",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClassificationOverride_RequestId",
                table: "DocumentClassificationOverrides",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClassificationOverrides_ActorUserId",
                table: "DocumentClassificationOverrides",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "UX_DocumentClassificationOverride_IdempotencyKey",
                table: "DocumentClassificationOverrides",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentClassificationOverrides");
        }
    }
}
