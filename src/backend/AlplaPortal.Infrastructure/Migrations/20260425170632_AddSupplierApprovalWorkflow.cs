using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdjustmentComment",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DafApprovedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DafApproverId",
                table: "Suppliers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DgApprovedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DgApproverId",
                table: "Suppliers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "Suppliers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "Suppliers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromStatusCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToStatusCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierStatusHistories_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierStatusHistories_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Suppliers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AdjustmentComment", "DafApprovedAtUtc", "DafApproverId", "DgApprovedAtUtc", "DgApproverId", "SubmittedAtUtc", "SubmittedByUserId" },
                values: new object[] { null, null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Suppliers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AdjustmentComment", "DafApprovedAtUtc", "DafApproverId", "DgApprovedAtUtc", "DgApproverId", "SubmittedAtUtc", "SubmittedByUserId" },
                values: new object[] { null, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_DafApproverId",
                table: "Suppliers",
                column: "DafApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_DgApproverId",
                table: "Suppliers",
                column: "DgApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierStatusHistories_ActorUserId",
                table: "SupplierStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierStatusHistories_OccurredAtUtc",
                table: "SupplierStatusHistories",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierStatusHistories_SupplierId",
                table: "SupplierStatusHistories",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Users_DafApproverId",
                table: "Suppliers",
                column: "DafApproverId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Users_DgApproverId",
                table: "Suppliers",
                column: "DgApproverId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Users_DafApproverId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Users_DgApproverId",
                table: "Suppliers");

            migrationBuilder.DropTable(
                name: "SupplierStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_DafApproverId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_DgApproverId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "AdjustmentComment",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DafApprovedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DafApproverId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DgApprovedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DgApproverId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "Suppliers");
        }
    }
}
