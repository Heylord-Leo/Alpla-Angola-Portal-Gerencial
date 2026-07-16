using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentManagers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentManagers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentManagers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepartmentManagers_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepartmentManagers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentManagers_DepartmentId_PlantId_IsActive",
                table: "DepartmentManagers",
                columns: new[] { "DepartmentId", "PlantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentManagers_DepartmentId_PlantId_UserId",
                table: "DepartmentManagers",
                columns: new[] { "DepartmentId", "PlantId", "UserId" },
                unique: true,
                filter: "[PlantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentManagers_DepartmentId_UserId_Global",
                table: "DepartmentManagers",
                columns: new[] { "DepartmentId", "UserId" },
                unique: true,
                filter: "[PlantId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentManagers_PlantId",
                table: "DepartmentManagers",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentManagers_UserId_IsActive",
                table: "DepartmentManagers",
                columns: new[] { "UserId", "IsActive" });

            // Seed (idempotent): every legacy Department.ResponsibleUserId becomes a GLOBAL
            // manager (PlantId NULL), preserving today's routing behavior bit for bit once
            // Phase B switches over. Inactive users are seeded too — the runtime resolution
            // filters them; hiding them here would mask a data problem instead of surfacing
            // it in the Master Data grid. ResponsibleUserId itself is not touched.
            migrationBuilder.Sql(@"
INSERT INTO DepartmentManagers (DepartmentId, PlantId, UserId, IsActive, CreatedAtUtc)
SELECT d.Id, NULL, d.ResponsibleUserId, 1, GETUTCDATE()
FROM Departments d
WHERE d.ResponsibleUserId IS NOT NULL
  AND EXISTS (SELECT 1 FROM Users u WHERE u.Id = d.ResponsibleUserId)
  AND NOT EXISTS (
      SELECT 1 FROM DepartmentManagers m
      WHERE m.DepartmentId = d.Id
        AND m.PlantId IS NULL
        AND m.UserId = d.ResponsibleUserId);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepartmentManagers");
        }
    }
}
