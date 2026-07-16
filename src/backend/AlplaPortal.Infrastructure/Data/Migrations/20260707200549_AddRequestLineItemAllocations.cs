using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestLineItemAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestLineItemAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    AllocationOrder = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLineItemAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestLineItemAllocations_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestLineItemAllocations_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestLineItemAllocations_RequestLineItems_RequestLineItemId",
                        column: x => x.RequestLineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_CostCenterId",
                table: "RequestLineItemAllocations",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_LineItemId",
                table: "RequestLineItemAllocations",
                column: "RequestLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItemAllocations_PlantId",
                table: "RequestLineItemAllocations",
                column: "PlantId");

            // --- Backfill: Create one 100% allocation record for every line item that: ---
            // 1. Has both PlantId AND CostCenterId set
            // 2. Is NOT deleted
            // 3. Does NOT already have an allocation record (idempotent guard)
            migrationBuilder.Sql(@"
                INSERT INTO [RequestLineItemAllocations]
                    ([Id], [RequestLineItemId], [PlantId], [CostCenterId], [Percentage],
                     [AllocationOrder], [CreatedAtUtc], [CreatedByUserId])
                SELECT
                    NEWID(),
                    li.[Id],
                    li.[PlantId],
                    li.[CostCenterId],
                    100.0000,
                    0,
                    GETUTCDATE(),
                    li.[CreatedByUserId]
                FROM [RequestLineItems] li
                WHERE li.[PlantId] IS NOT NULL
                  AND li.[CostCenterId] IS NOT NULL
                  AND li.[IsDeleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [RequestLineItemAllocations] a
                      WHERE a.[RequestLineItemId] = li.[Id]
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestLineItemAllocations");
        }
    }
}
