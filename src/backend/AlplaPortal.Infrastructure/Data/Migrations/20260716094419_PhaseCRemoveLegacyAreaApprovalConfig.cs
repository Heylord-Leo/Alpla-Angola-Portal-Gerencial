using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseCRemoveLegacyAreaApprovalConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Phase C audit snapshots (idempotent) ─────────────────────────────
            // 1. Legacy Department.ResponsibleUserId values, for audit / partial manual
            //    rollback. The Phase A migration already seeded these users as GLOBAL
            //    DepartmentManagers, so no functional data is lost by the drop.
            // 2. Manual "Area Approver" role assignments about to be deleted — the role
            //    is derived-only from DepartmentManagers as of Phase B/C. The role row
            //    itself stays in Roles (the derived claim and [Authorize] use its name).
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo._PhaseC_DepartmentResponsibleBackup', 'U') IS NULL
    CREATE TABLE dbo._PhaseC_DepartmentResponsibleBackup (
        DepartmentId int NOT NULL,
        ResponsibleUserId uniqueidentifier NOT NULL,
        BackedUpAtUtc datetime2 NOT NULL DEFAULT GETUTCDATE());

INSERT INTO dbo._PhaseC_DepartmentResponsibleBackup (DepartmentId, ResponsibleUserId)
SELECT d.Id, d.ResponsibleUserId
FROM Departments d
WHERE d.ResponsibleUserId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo._PhaseC_DepartmentResponsibleBackup b
                  WHERE b.DepartmentId = d.Id AND b.ResponsibleUserId = d.ResponsibleUserId);

IF OBJECT_ID('dbo._PhaseC_AreaApproverManualAssignmentsBackup', 'U') IS NULL
    CREATE TABLE dbo._PhaseC_AreaApproverManualAssignmentsBackup (
        UserId uniqueidentifier NOT NULL,
        RoleId int NOT NULL,
        RemovedAtUtc datetime2 NOT NULL DEFAULT GETUTCDATE());

INSERT INTO dbo._PhaseC_AreaApproverManualAssignmentsBackup (UserId, RoleId)
SELECT ura.UserId, ura.RoleId
FROM UserRoleAssignments ura
JOIN Roles r ON r.Id = ura.RoleId
WHERE r.RoleName = 'Area Approver'
  AND NOT EXISTS (SELECT 1 FROM dbo._PhaseC_AreaApproverManualAssignmentsBackup b
                  WHERE b.UserId = ura.UserId AND b.RoleId = ura.RoleId);

DELETE ura
FROM UserRoleAssignments ura
JOIN Roles r ON r.Id = ura.RoleId
WHERE r.RoleName = 'Area Approver';
");

            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Users_ResponsibleUserId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_ResponsibleUserId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "Departments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResponsibleUserId",
                table: "Departments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 1,
                column: "ResponsibleUserId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 2,
                column: "ResponsibleUserId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 3,
                column: "ResponsibleUserId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 4,
                column: "ResponsibleUserId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ResponsibleUserId",
                table: "Departments",
                column: "ResponsibleUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Users_ResponsibleUserId",
                table: "Departments",
                column: "ResponsibleUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Partial data rollback (documented limitation) ────────────────────
            // Restores manual "Area Approver" assignments from the audit snapshot, if
            // present. ResponsibleUserId VALUES are NOT reconstructed automatically —
            // multiple managers per department/plant may now exist, so no single value
            // is authoritative; restore manually from _PhaseC_DepartmentResponsibleBackup
            // if truly needed.
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo._PhaseC_AreaApproverManualAssignmentsBackup', 'U') IS NOT NULL
INSERT INTO UserRoleAssignments (UserId, RoleId)
SELECT b.UserId, b.RoleId
FROM dbo._PhaseC_AreaApproverManualAssignmentsBackup b
WHERE EXISTS (SELECT 1 FROM Users u WHERE u.Id = b.UserId)
  AND EXISTS (SELECT 1 FROM Roles r WHERE r.Id = b.RoleId)
  AND NOT EXISTS (SELECT 1 FROM UserRoleAssignments ura
                  WHERE ura.UserId = b.UserId AND ura.RoleId = b.RoleId);
");
        }
    }
}
