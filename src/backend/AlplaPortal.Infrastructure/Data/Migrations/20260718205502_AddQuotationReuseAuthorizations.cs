using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationReuseAuthorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuotationReuseAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConsumedByApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationReuseAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuotationReuseAuthorizations_ApprovalBatches_SourceApprovalBatchId",
                        column: x => x.SourceApprovalBatchId,
                        principalTable: "ApprovalBatches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuotationReuseAuthorizations_QuotationItems_QuotationItemId",
                        column: x => x.QuotationItemId,
                        principalTable: "QuotationItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuotationReuseAuthorizations_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuotationReuseAuthorizations_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationReuseAuth_IsActive",
                table: "QuotationReuseAuthorizations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationReuseAuth_QuotationId",
                table: "QuotationReuseAuthorizations",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationReuseAuth_QuotationItemId",
                table: "QuotationReuseAuthorizations",
                column: "QuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationReuseAuth_RequestId",
                table: "QuotationReuseAuthorizations",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationReuseAuth_SourceBatchId",
                table: "QuotationReuseAuthorizations",
                column: "SourceApprovalBatchId");

            migrationBuilder.CreateIndex(
                name: "UX_QuotationReuseAuth_Item_SourceBatch_Active",
                table: "QuotationReuseAuthorizations",
                columns: new[] { "QuotationItemId", "SourceApprovalBatchId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuotationReuseAuthorizations");
        }
    }
}
