using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHRLeaveModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HREmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InnuxEmployeeId = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InnuxDepartmentName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InnuxDepartmentId = table.Column<int>(type: "int", nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    HireDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PortalDepartmentId = table.Column<int>(type: "int", nullable: true),
                    PlantId = table.Column<int>(type: "int", nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsMapped = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HREmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HREmployees_Departments_PortalDepartmentId",
                        column: x => x.PortalDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HREmployees_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HREmployees_Users_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HRSyncLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmployeesCreated = table.Column<int>(type: "int", nullable: false),
                    EmployeesUpdated = table.Column<int>(type: "int", nullable: false),
                    EmployeesDeactivated = table.Column<int>(type: "int", nullable: false),
                    TotalProcessed = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorDetails = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HRSyncLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HRSyncLogs_Users_TriggeredByUserId",
                        column: x => x.TriggeredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayNamePt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountsAgainstBalance = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalDays = table.Column<int>(type: "int", nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RejectedReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRecords_HREmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "HREmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRecords_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRecords_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRecords_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveStatusHistories_LeaveRecords_LeaveRecordId",
                        column: x => x.LeaveRecordId,
                        principalTable: "LeaveRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaveStatusHistories_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LeaveTypes",
                columns: new[] { "Id", "Code", "Color", "CountsAgainstBalance", "DisplayNamePt", "DisplayOrder", "IsActive" },
                values: new object[,]
                {
                    { 1, "VACATION", "#3b82f6", true, "Férias", 1, true },
                    { 2, "SICK_LEAVE", "#ef4444", false, "Licença Médica", 2, true },
                    { 3, "JUSTIFIED_ABSENCE", "#f59e0b", false, "Falta Justificada", 3, true },
                    { 4, "UNJUSTIFIED_ABSENCE", "#dc2626", false, "Falta Injustificada", 4, true },
                    { 5, "PERSONAL_LEAVE", "#8b5cf6", true, "Licença Pessoal", 5, true },
                    { 6, "COMPENSATION_DAY", "#06b6d4", false, "Dia de Compensação", 6, true },
                    { 7, "OTHER", "#6b7280", false, "Outros", 7, true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_EmployeeCode",
                table: "HREmployees",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_InnuxEmployeeId",
                table: "HREmployees",
                column: "InnuxEmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_IsActive",
                table: "HREmployees",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_IsMapped",
                table: "HREmployees",
                column: "IsMapped");

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_ManagerUserId",
                table: "HREmployees",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_PlantId",
                table: "HREmployees",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_HREmployees_PortalDepartmentId",
                table: "HREmployees",
                column: "PortalDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_HRSyncLogs_TriggeredByUserId",
                table: "HRSyncLogs",
                column: "TriggeredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_ApprovedByUserId",
                table: "LeaveRecords",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_EmployeeId",
                table: "LeaveRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_EndDate",
                table: "LeaveRecords",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_LeaveTypeId",
                table: "LeaveRecords",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_RequestedByUserId",
                table: "LeaveRecords",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_StartDate",
                table: "LeaveRecords",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_StatusCode",
                table: "LeaveRecords",
                column: "StatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveStatusHistories_ActorUserId",
                table: "LeaveStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveStatusHistories_LeaveRecordId",
                table: "LeaveStatusHistories",
                column: "LeaveRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Code",
                table: "LeaveTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HRSyncLogs");

            migrationBuilder.DropTable(
                name: "LeaveStatusHistories");

            migrationBuilder.DropTable(
                name: "LeaveRecords");

            migrationBuilder.DropTable(
                name: "HREmployees");

            migrationBuilder.DropTable(
                name: "LeaveTypes");
        }
    }
}
