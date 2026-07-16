using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestPoGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestPoGroupId",
                table: "RequestReconciliations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestPoGroupId",
                table: "RequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestPoGroupId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SelectedQuotationItemId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestPoGroupId",
                table: "RequestAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestPoGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SupplierNifSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentConditionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AdvancePaymentPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPoGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPoGroups_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPoGroups_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestPoGroups_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestReconciliations_RequestPoGroupId",
                table: "RequestReconciliations",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_RequestPoGroupId",
                table: "RequestPayments",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_RequestPoGroupId",
                table: "RequestLineItems",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_SelectedQuotationItemId",
                table: "RequestLineItems",
                column: "SelectedQuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_RequestPoGroupId",
                table: "RequestAttachments",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_CurrencyId",
                table: "RequestPoGroups",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_RequestId",
                table: "RequestPoGroups",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_SupplierId",
                table: "RequestPoGroups",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestAttachments_RequestPoGroups_RequestPoGroupId",
                table: "RequestAttachments",
                column: "RequestPoGroupId",
                principalTable: "RequestPoGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_QuotationItems_SelectedQuotationItemId",
                table: "RequestLineItems",
                column: "SelectedQuotationItemId",
                principalTable: "QuotationItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_RequestPoGroups_RequestPoGroupId",
                table: "RequestLineItems",
                column: "RequestPoGroupId",
                principalTable: "RequestPoGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestPayments_RequestPoGroups_RequestPoGroupId",
                table: "RequestPayments",
                column: "RequestPoGroupId",
                principalTable: "RequestPoGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestReconciliations_RequestPoGroups_RequestPoGroupId",
                table: "RequestReconciliations",
                column: "RequestPoGroupId",
                principalTable: "RequestPoGroups",
                principalColumn: "Id");

            // --- DATA MIGRATION LOGIC ---
            migrationBuilder.Sql(@"
                -- Create default PO Group using the Request's existing fields
                INSERT INTO [RequestPoGroups] (
                    [Id], [RequestId], [SupplierId], [SupplierNameSnapshot], [SupplierNifSnapshot],
                    [CurrencyId], [CurrencyCode], [PaymentConditionCode], [AdvancePaymentPercent],
                    [Status], [TotalAmount], [PurchaseOrderNumber], 
                    [CreatedAtUtc], [CreatedByUserId]
                )
                SELECT 
                    NEWID(), 
                    R.[Id], 
                    Q.[SupplierId], 
                    Q.[SupplierNameSnapshot], 
                    NULL, 
                    NULL, -- No simple join to get CurrencyId, leave NULL for legacy
                    Q.[Currency], 
                    R.[PaymentConditionCode], 
                    R.[AdvancePaymentPercent],
                    'PENDING',
                    R.[ApprovedTotalAmount], 
                    NULL, -- PurchaseOrderNumber (not on Request, leave NULL)
                    GETUTCDATE(),
                    R.[CreatedByUserId]
                FROM [Requests] R
                LEFT JOIN [Quotations] Q ON R.SelectedQuotationId = Q.Id
                JOIN [RequestStatuses] RS ON R.StatusId = RS.Id
                WHERE RS.[Code] IN (
                    'APPROVED', 'PO_ISSUED', 'WAITING_PO_CORRECTION', 
                    'PAYMENT_REQUEST_SENT', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 
                    'ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_COMPLETED', 
                    'WAITING_SUPPLIER_DELIVERY', 'WAITING_RECEIPT', 'IN_FOLLOWUP', 
                    'WAITING_RECONCILIATION', 'COMPLETED'
                );

                -- After creating PO groups, update the children tables to link to the new group
                UPDATE L
                SET [RequestPoGroupId] = G.[Id]
                FROM [RequestLineItems] L
                JOIN [RequestPoGroups] G ON L.[RequestId] = G.[RequestId];

                -- Update RequestAttachments (where Type = PO, etc)
                UPDATE A
                SET [RequestPoGroupId] = G.[Id]
                FROM [RequestAttachments] A
                JOIN [RequestPoGroups] G ON A.[RequestId] = G.[RequestId]
                WHERE A.[AttachmentTypeCode] IN ('PO', 'ADVANCE_PAYMENT_PROOF', 'PAYMENT_PROOF', 'RECEIPT');

                -- Update RequestPayments
                UPDATE P
                SET [RequestPoGroupId] = G.[Id]
                FROM [RequestPayments] P
                JOIN [RequestPoGroups] G ON P.[RequestId] = G.[RequestId];

                -- Update RequestReconciliations
                UPDATE Rec
                SET [RequestPoGroupId] = G.[Id]
                FROM [RequestReconciliations] Rec
                JOIN [RequestPoGroups] G ON Rec.[RequestId] = G.[RequestId];
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The production rollback must be non-destructive and must not drop the new nullable columns/tables
            // unless a full database restore is planned. So the destructive drops have been removed.
            
            // Revert data migration (optional, but requested non-destructive)
            /*
            migrationBuilder.Sql(@"
                UPDATE [RequestLineItems] SET [RequestPoGroupId] = NULL;
                UPDATE [RequestAttachments] SET [RequestPoGroupId] = NULL;
                UPDATE [RequestPayments] SET [RequestPoGroupId] = NULL;
                UPDATE [RequestReconciliations] SET [RequestPoGroupId] = NULL;
                DELETE FROM [RequestPoGroups];
            ");
            */
        }
    }
}
