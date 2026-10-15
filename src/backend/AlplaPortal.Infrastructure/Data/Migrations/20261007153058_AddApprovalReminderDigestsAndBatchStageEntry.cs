using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalReminderDigestsAndBatchStageEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StageEnteredAtUtc",
                table: "ApprovalBatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalReminderRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LocalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UnitsConsidered = table.Column<int>(type: "int", nullable: false),
                    UnitsEligible = table.Column<int>(type: "int", nullable: false),
                    UnitsWithoutStageEntry = table.Column<int>(type: "int", nullable: false),
                    UnitsWithoutRecipient = table.Column<int>(type: "int", nullable: false),
                    Recipients = table.Column<int>(type: "int", nullable: false),
                    DigestsQueued = table.Column<int>(type: "int", nullable: false),
                    DigestsDryRun = table.Column<int>(type: "int", nullable: false),
                    SkippedDedup = table.Column<int>(type: "int", nullable: false),
                    SkippedAllowList = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalReminderRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalReminderDigests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DigestDateLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    OverflowCount = table.Column<int>(type: "int", nullable: false),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PayloadHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutboxEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalReminderDigests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalReminderDigests_ApprovalReminderRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ApprovalReminderRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalReminderDigests_EmailOutbox_OutboxEntryId",
                        column: x => x.OutboxEntryId,
                        principalTable: "EmailOutbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApprovalReminderDigests_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalReminderDigestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DigestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BatchNumber = table.Column<int>(type: "int", nullable: true),
                    RequestNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StageEnteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DaysPending = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalReminderDigestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalReminderDigestItems_ApprovalReminderDigests_DigestId",
                        column: x => x.DigestId,
                        principalTable: "ApprovalReminderDigests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderDigestItems_DigestId",
                table: "ApprovalReminderDigestItems",
                column: "DigestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderDigestItems_RequestId",
                table: "ApprovalReminderDigestItems",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderDigests_Dedup",
                table: "ApprovalReminderDigests",
                columns: new[] { "RecipientUserId", "DigestDateLocal", "DryRun" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderDigests_OutboxEntryId",
                table: "ApprovalReminderDigests",
                column: "OutboxEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderDigests_RunId",
                table: "ApprovalReminderDigests",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReminderRuns_LocalDate_StartedAtUtc",
                table: "ApprovalReminderRuns",
                columns: new[] { "LocalDate", "StartedAtUtc" });
            migrationBuilder.Sql(StageEntryBackfillSql);
        }

        /// <summary>
        /// Deterministic backfill of ApprovalBatches.StageEnteredAtUtc for batches currently waiting in
        /// an approval stage, from reliable transition evidence only:
        ///  R1 WAITING_FINAL_APPROVAL → latest BATCH_AREA_APPROVED history row of that lot
        ///     ("Aprovação da Área do Lote #N realizada…").
        ///  R2 WAITING_AREA_APPROVAL, resubmitted → latest BATCH_RESUBMITTED history row of that lot
        ///     ("Lote #N reenviado para aprovação da área…").
        ///  R3 WAITING_AREA_APPROVAL, never left the area stage (no adjustment/resubmit/edit/area-approval
        ///     history row mentions the lot) → the batch's own CreatedAtUtc.
        /// Anything else stays NULL and is reported by scripts/db/approval-batch-stage-entry-preflight-readonly.sql;
        /// NULL units are excluded from reminder digests (never substituted with an unrelated date).
        /// Idempotent: only NULL values are written. Settled batches (APPROVED/REJECTED/…) are not touched.
        /// </summary>
        public const string StageEntryBackfillSql = @"
-- R1: final stage entry = area approval of this lot
UPDATE b SET StageEnteredAtUtc = ev.EnteredAt
FROM dbo.ApprovalBatches b
CROSS APPLY (
    SELECT MAX(h.CreatedAtUtc) AS EnteredAt
    FROM dbo.RequestStatusHistories h
    WHERE h.RequestId = b.RequestId
      AND h.ActionTaken = N'BATCH_AREA_APPROVED'
      AND h.Comment LIKE N'Aprovação da Área do Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' realizada%'
) ev
WHERE b.Status = N'WAITING_FINAL_APPROVAL' AND b.StageEnteredAtUtc IS NULL AND ev.EnteredAt IS NOT NULL;

-- R2: area stage re-entry = latest resubmission of this lot
UPDATE b SET StageEnteredAtUtc = ev.EnteredAt
FROM dbo.ApprovalBatches b
CROSS APPLY (
    SELECT MAX(h.CreatedAtUtc) AS EnteredAt
    FROM dbo.RequestStatusHistories h
    WHERE h.RequestId = b.RequestId
      AND h.ActionTaken = N'BATCH_RESUBMITTED'
      AND h.Comment LIKE N'Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' reenviado para aprovação da área%'
) ev
WHERE b.Status = N'WAITING_AREA_APPROVAL' AND b.StageEnteredAtUtc IS NULL AND ev.EnteredAt IS NOT NULL;

-- R3: area stage, never left it → creation moment is the stage entry
UPDATE b SET StageEnteredAtUtc = b.CreatedAtUtc
FROM dbo.ApprovalBatches b
WHERE b.Status = N'WAITING_AREA_APPROVAL' AND b.StageEnteredAtUtc IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RequestStatusHistories h
      WHERE h.RequestId = b.RequestId
        AND h.ActionTaken IN (N'BATCH_AREA_ADJUSTMENT', N'BATCH_RESUBMITTED', N'BATCH_EDITED', N'BATCH_AREA_APPROVED', N'BATCH_AREA_REJECTED', N'BATCH_FINAL_ADJUSTMENT', N'BATCH_FINAL_APPROVED', N'BATCH_FINAL_REJECTED')
        AND h.Comment LIKE N'%Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N'[^0-9]%'
  );
";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalReminderDigestItems");

            migrationBuilder.DropTable(
                name: "ApprovalReminderDigests");

            migrationBuilder.DropTable(
                name: "ApprovalReminderRuns");

            migrationBuilder.DropColumn(
                name: "StageEnteredAtUtc",
                table: "ApprovalBatches");
        }
    }
}
