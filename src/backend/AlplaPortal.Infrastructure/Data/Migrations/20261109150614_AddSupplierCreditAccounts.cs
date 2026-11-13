using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCreditAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditCoverageAmount",
                table: "RequestPoGroups",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierCreditReservationId",
                table: "RequestPoGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementStage",
                table: "RequestPayments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierCreditReservationId",
                table: "RequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierCreditAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditAccounts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdvanceCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BalanceCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AdvanceAppliedEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BalanceAppliedEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleasedHoldAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReversedAppliedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReversalKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReleaseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersededByReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAdvancePaymentId = table.Column<int>(type: "int", nullable: true),
                    ReopenedBalancePaymentId = table.Column<int>(type: "int", nullable: true),
                    ReservedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditReservations_RequestPoGroups_RequestPoGroupId",
                        column: x => x.RequestPoGroupId,
                        principalTable: "RequestPoGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditReservations_SupplierCreditAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "SupplierCreditAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestPoGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RequestNumberSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PurchaseOrderNumberSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginPlantId = table.Column<int>(type: "int", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditEntries_SupplierCreditAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "SupplierCreditAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditEntries_SupplierCreditReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "SupplierCreditReservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPoGroups_SupplierCreditReservationId",
                table: "RequestPoGroups",
                column: "SupplierCreditReservationId",
                filter: "[SupplierCreditReservationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPayments_SupplierCreditReservationId",
                table: "RequestPayments",
                column: "SupplierCreditReservationId",
                filter: "[SupplierCreditReservationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditAccounts_CompanyId",
                table: "SupplierCreditAccounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_SupplierCreditAccounts_AccountNumber",
                table: "SupplierCreditAccounts",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SupplierCreditAccounts_Supplier_Company_Currency",
                table: "SupplierCreditAccounts",
                columns: new[] { "SupplierId", "CompanyId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditEntries_AccountId",
                table: "SupplierCreditEntries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditEntries_RequestPoGroupId",
                table: "SupplierCreditEntries",
                column: "RequestPoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditEntries_ReservationId",
                table: "SupplierCreditEntries",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "UX_SupplierCreditEntries_IdempotencyKey",
                table: "SupplierCreditEntries",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditReservations_AccountId",
                table: "SupplierCreditReservations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditReservations_RequestId",
                table: "SupplierCreditReservations",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "UX_SupplierCreditReservations_LiveGroup",
                table: "SupplierCreditReservations",
                column: "RequestPoGroupId",
                unique: true,
                filter: "[Status] IN ('RESERVED','PARTIALLY_APPLIED','APPLIED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierCreditEntries");

            migrationBuilder.DropTable(
                name: "SupplierCreditReservations");

            migrationBuilder.DropTable(
                name: "SupplierCreditAccounts");

            migrationBuilder.DropIndex(
                name: "IX_RequestPoGroups_SupplierCreditReservationId",
                table: "RequestPoGroups");

            migrationBuilder.DropIndex(
                name: "IX_RequestPayments_SupplierCreditReservationId",
                table: "RequestPayments");

            migrationBuilder.DropColumn(
                name: "CreditCoverageAmount",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "SupplierCreditReservationId",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "SettlementStage",
                table: "RequestPayments");

            migrationBuilder.DropColumn(
                name: "SupplierCreditReservationId",
                table: "RequestPayments");
        }
    }
}
