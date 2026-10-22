using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationSubmissionMarker : Migration
    {
        /// <summary>
        /// §7 durable submission marker — one-time backfill from the references that still exist at migration time
        /// (selected items and candidates of ANY approval batch, whatever its status — rejected and cancelled included;
        /// legacy request-level awards via RequestLineItems.SelectedQuotationItemId). Idempotent: only rows still NULL.
        /// Evidence vs estimate: batch-backed rows get the batch's CreatedAtUtc and FirstSubmittedSource = BACKFILL_BATCH —
        /// persisted batch evidence, equal to the submission instant only for quotations present at batch creation
        /// (rework edits and extra-item inclusions join an existing batch later), so a lower bound rather than the exact
        /// first-submission time; legacy award rows get FirstSubmittedSource = BACKFILL_LINE_AWARD_ESTIMATED with an
        /// APPROXIMATE date (line UpdatedAtUtc, else quotation CreatedAtUtc) — proven submission, estimated time.
        /// Several batches: deterministic earliest by (CreatedAtUtc, BatchNumber, BatchId); the pointer and the
        /// timestamp always come from that same row. Boundary: a quotation whose references were all removed BEFORE
        /// this migration (batch recomposition, extra-item reversal, batch deletion) cannot be recovered here and stays
        /// NULL — nothing is inferred from supplier names or free-text history. Exposed as a constant so the relational
        /// test executes the same text.
        /// </summary>
        public const string BackfillSql = @"
;WITH refs AS (
    SELECT qi.QuotationId, b.Id AS BatchId, b.CreatedAtUtc, b.BatchNumber
    FROM dbo.ApprovalBatchItemCandidates c
    JOIN dbo.ApprovalBatchItems bi ON bi.Id = c.ApprovalBatchItemId
    JOIN dbo.ApprovalBatches b ON b.Id = bi.ApprovalBatchId
    JOIN dbo.QuotationItems qi ON qi.Id = c.QuotationItemId
    UNION ALL
    SELECT qi.QuotationId, b.Id, b.CreatedAtUtc, b.BatchNumber
    FROM dbo.ApprovalBatchItems bi
    JOIN dbo.ApprovalBatches b ON b.Id = bi.ApprovalBatchId
    JOIN dbo.QuotationItems qi ON qi.Id = bi.SelectedQuotationItemId
    WHERE bi.SelectedQuotationItemId IS NOT NULL
), firstRef AS (
    SELECT QuotationId, BatchId, CreatedAtUtc,
           ROW_NUMBER() OVER (PARTITION BY QuotationId ORDER BY CreatedAtUtc, BatchNumber, BatchId) AS rn
    FROM refs
)
UPDATE q
   SET q.FirstSubmittedForApprovalAtUtc = f.CreatedAtUtc,
       q.FirstSubmittedBatchId = f.BatchId,
       q.FirstSubmittedSource = N'BACKFILL_BATCH'
  FROM dbo.Quotations q
  JOIN firstRef f ON f.QuotationId = q.Id AND f.rn = 1
 WHERE q.FirstSubmittedForApprovalAtUtc IS NULL;

-- Legacy request-level awards (no batch): proven submission, ESTIMATED date = line UpdatedAtUtc, else the quotation's CreatedAtUtc.
UPDATE q
   SET q.FirstSubmittedForApprovalAtUtc = x.FirstAt,
       q.FirstSubmittedBatchId = NULL,
       q.FirstSubmittedSource = N'BACKFILL_LINE_AWARD_ESTIMATED'
  FROM dbo.Quotations q
  JOIN (SELECT qi.QuotationId, MIN(ISNULL(li.UpdatedAtUtc, q2.CreatedAtUtc)) AS FirstAt
          FROM dbo.RequestLineItems li
          JOIN dbo.QuotationItems qi ON qi.Id = li.SelectedQuotationItemId
          JOIN dbo.Quotations q2 ON q2.Id = qi.QuotationId
         WHERE li.SelectedQuotationItemId IS NOT NULL
         GROUP BY qi.QuotationId) x ON x.QuotationId = q.Id
 WHERE q.FirstSubmittedForApprovalAtUtc IS NULL;
";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FirstSubmittedBatchId",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSubmittedForApprovalAtUtc",
                table: "Quotations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstSubmittedSource",
                table: "Quotations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Quotations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_FirstSubmittedBatchId",
                table: "Quotations",
                column: "FirstSubmittedBatchId");

            // Backfill from the references that exist now (see BackfillSql remarks for evidence vs estimate and the boundary).
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_FirstSubmittedBatchId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "FirstSubmittedBatchId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "FirstSubmittedForApprovalAtUtc",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "FirstSubmittedSource",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Quotations");
        }
    }
}
