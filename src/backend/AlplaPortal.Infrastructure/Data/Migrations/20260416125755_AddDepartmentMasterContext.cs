using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentMasterContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartmentMasterId",
                table: "HREmployees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DepartmentMasters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceSystem = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceDatabase = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DepartmentName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentMasters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_DepartmentMasterId",
                table: "HREmployees",
                column: "DepartmentMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMasters_SourceSystem_SourceDatabase_DepartmentCode",
                table: "DepartmentMasters",
                columns: new[] { "SourceSystem", "SourceDatabase", "DepartmentCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HREmployees_DepartmentMasters_DepartmentMasterId",
                table: "HREmployees",
                column: "DepartmentMasterId",
                principalTable: "DepartmentMasters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HREmployees_DepartmentMasters_DepartmentMasterId",
                table: "HREmployees");

            migrationBuilder.DropTable(
                name: "DepartmentMasters");

            migrationBuilder.DropIndex(
                name: "IX_HREmployees_DepartmentMasterId",
                table: "HREmployees");

            migrationBuilder.DropColumn(
                name: "DepartmentMasterId",
                table: "HREmployees");
        }
    }
}
