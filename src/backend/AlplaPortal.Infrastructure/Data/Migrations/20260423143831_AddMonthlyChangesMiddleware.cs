using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyChangesMiddleware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: ContractDocuments soft-delete columns (DeletedAtUtc, DeletedByUserId, IsDeleted)
            // already exist in the database from migration 20260421155149_AddContractDocumentSoftDelete.
            // The EF model snapshot has been updated to include them.

            migrationBuilder.CreateTable(
                name: "MCDetectionThresholds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThresholdType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ScheduleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    ThresholdMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCDetectionThresholds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MCPrimaveraCodeMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurrenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MinDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    MaxDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    PrimaveraCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PrimaveraCodeDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultHoursFormula = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    ScheduleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCPrimaveraCodeMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MCProcessingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SyncedRowCount = table.Column<int>(type: "int", nullable: false),
                    DetectionCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    AnomalyCount = table.Column<int>(type: "int", nullable: false),
                    UnresolvedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCProcessingRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCProcessingRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MCAttendanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InnuxAlteracaoId = table.Column<int>(type: "int", nullable: false),
                    InnuxEmployeeId = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ScheduleSigla = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsRestDay = table.Column<bool>(type: "bit", nullable: false),
                    FirstEntry = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    FirstExit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ExpectedMinutes = table.Column<int>(type: "int", nullable: false),
                    AbsenceMinutes = table.Column<int>(type: "int", nullable: false),
                    JustifiedAbsenceMinutes = table.Column<int>(type: "int", nullable: false),
                    BalanceMinutes = table.Column<int>(type: "int", nullable: false),
                    PunchCount = table.Column<int>(type: "int", nullable: false),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    AnomalyDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCAttendanceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCAttendanceSnapshots_MCProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "MCProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MCExportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneratedByEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportConfirmedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfigSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfigSnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCExportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCExportBatches_MCProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "MCProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MCProcessingLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCProcessingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCProcessingLogs_MCProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "MCProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MCMonthlyChangeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OccurrenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DetectionRule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ScheduleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StatusCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrimaveraCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PrimaveraCodeDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    OriginalPrimaveraCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OriginalHours = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    OverrideBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OverrideAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsAnomaly = table.Column<bool>(type: "bit", nullable: false),
                    AnomalyReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExclusionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExcludedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExcludedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCMonthlyChangeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCMonthlyChangeItems_MCAttendanceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "MCAttendanceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MCMonthlyChangeItems_MCProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "MCProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MCExportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthlyChangeItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowOrder = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrimaveraCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WasManualOverride = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MCExportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MCExportRows_MCExportBatches_ExportBatchId",
                        column: x => x.ExportBatchId,
                        principalTable: "MCExportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MCExportRows_MCMonthlyChangeItems_MonthlyChangeItemId",
                        column: x => x.MonthlyChangeItemId,
                        principalTable: "MCMonthlyChangeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MCAttendanceSnapshots_ProcessingRunId_Date",
                table: "MCAttendanceSnapshots",
                columns: new[] { "ProcessingRunId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MCAttendanceSnapshots_Run_Employee_Date",
                table: "MCAttendanceSnapshots",
                columns: new[] { "ProcessingRunId", "InnuxEmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MCDetectionThresholds_Lookup",
                table: "MCDetectionThresholds",
                columns: new[] { "ThresholdType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MCExportBatches_GeneratedAtUtc",
                table: "MCExportBatches",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MCExportBatches_ProcessingRunId",
                table: "MCExportBatches",
                column: "ProcessingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MCExportRows_ExportBatchId",
                table: "MCExportRows",
                column: "ExportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MCExportRows_MonthlyChangeItemId",
                table: "MCExportRows",
                column: "MonthlyChangeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MCMonthlyChangeItems_Anomalies",
                table: "MCMonthlyChangeItems",
                column: "IsAnomaly",
                filter: "[IsAnomaly] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MCMonthlyChangeItems_ProcessingRunId_Date",
                table: "MCMonthlyChangeItems",
                columns: new[] { "ProcessingRunId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MCMonthlyChangeItems_ProcessingRunId_EmployeeCode",
                table: "MCMonthlyChangeItems",
                columns: new[] { "ProcessingRunId", "EmployeeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MCMonthlyChangeItems_ProcessingRunId_StatusCode",
                table: "MCMonthlyChangeItems",
                columns: new[] { "ProcessingRunId", "StatusCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MCMonthlyChangeItems_SnapshotId",
                table: "MCMonthlyChangeItems",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MCPrimaveraCodeMappings_Lookup",
                table: "MCPrimaveraCodeMappings",
                columns: new[] { "OccurrenceType", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingLogs_EventType",
                table: "MCProcessingLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingLogs_ProcessingRunId_OccurredAtUtc",
                table: "MCProcessingLogs",
                columns: new[] { "ProcessingRunId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingRuns_CreatedAtUtc",
                table: "MCProcessingRuns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingRuns_CreatedByUserId",
                table: "MCProcessingRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingRuns_EntityId_Year_Month",
                table: "MCProcessingRuns",
                columns: new[] { "EntityId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_MCProcessingRuns_StatusCode",
                table: "MCProcessingRuns",
                column: "StatusCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MCDetectionThresholds");

            migrationBuilder.DropTable(
                name: "MCExportRows");

            migrationBuilder.DropTable(
                name: "MCPrimaveraCodeMappings");

            migrationBuilder.DropTable(
                name: "MCProcessingLogs");

            migrationBuilder.DropTable(
                name: "MCExportBatches");

            migrationBuilder.DropTable(
                name: "MCMonthlyChangeItems");

            migrationBuilder.DropTable(
                name: "MCAttendanceSnapshots");

            migrationBuilder.DropTable(
                name: "MCProcessingRuns");

            // Note: ContractDocuments soft-delete columns are NOT dropped here.
            // They were created by migration 20260421155149 and are managed independently.
        }
    }
}
