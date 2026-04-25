using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierRegistrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIban",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankSwift",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail1",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail2",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName1",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName2",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone1",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone2",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactRole1",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactRole2",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationStatus",
                table: "Suppliers",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierDocuments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierDocuments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Suppliers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Address", "BankAccountNumber", "BankIban", "BankSwift", "ContactEmail1", "ContactEmail2", "ContactName1", "ContactName2", "ContactPhone1", "ContactPhone2", "ContactRole1", "ContactRole2", "CreatedAtUtc", "CreatedByUserId", "Notes", "PaymentMethod", "PaymentTerms", "RegistrationStatus", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, null, null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "ACTIVE", null, null });

            migrationBuilder.UpdateData(
                table: "Suppliers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Address", "BankAccountNumber", "BankIban", "BankSwift", "ContactEmail1", "ContactEmail2", "ContactName1", "ContactName2", "ContactPhone1", "ContactPhone2", "ContactRole1", "ContactRole2", "CreatedAtUtc", "CreatedByUserId", "Notes", "PaymentMethod", "PaymentTerms", "RegistrationStatus", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, null, null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "ACTIVE", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_RegistrationStatus",
                table: "Suppliers",
                column: "RegistrationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_FileHash",
                table: "SupplierDocuments",
                column: "FileHash",
                filter: "[FileHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_SupplierId",
                table: "SupplierDocuments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_SupplierId_DocumentType",
                table: "SupplierDocuments",
                columns: new[] { "SupplierId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_UploadedByUserId",
                table: "SupplierDocuments",
                column: "UploadedByUserId");

            // Data migration: set all existing supplier records to ACTIVE
            // to preserve backward compatibility with procurement flows
            migrationBuilder.Sql(@"
                UPDATE [Suppliers]
                SET [RegistrationStatus] = 'ACTIVE',
                    [CreatedAtUtc] = CASE WHEN [CreatedAtUtc] = '0001-01-01' THEN GETUTCDATE() ELSE [CreatedAtUtc] END
                WHERE [RegistrationStatus] = '' OR [RegistrationStatus] IS NULL
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_RegistrationStatus",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "BankIban",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "BankSwift",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactEmail1",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactEmail2",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactName1",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactName2",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactPhone1",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactPhone2",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactRole1",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactRole2",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "RegistrationStatus",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Suppliers");
        }
    }
}
