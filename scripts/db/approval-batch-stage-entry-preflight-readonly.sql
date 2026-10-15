/* ============================================================================================
   approval-batch-stage-entry-preflight-readonly.sql  (READ-ONLY — SELECTs only, no writes)

   Purpose : Before (or after) applying migration AddApprovalReminderDigestsAndBatchStageEntry,
             show how its deterministic StageEnteredAtUtc backfill classifies every batch that is
             currently waiting in an approval stage, and list the batches that CANNOT be
             established from reliable evidence (they stay NULL and are excluded from reminder
             digests — never substituted with an unrelated date).
   Rules   : R1 WAITING_FINAL_APPROVAL → latest BATCH_AREA_APPROVED row of the lot
             R2 WAITING_AREA_APPROVAL   → latest BATCH_RESUBMITTED row of the lot
             R3 WAITING_AREA_APPROVAL, never left the area stage → batch CreatedAtUtc
             R0 none of the above → NULL (reported in section C)
   Safe on : TEST [Portal-Gerencial-Test] and PROD [Portal-Gerencial] (read-only).
   Run pre-migration: column StageEnteredAtUtc may not exist yet → the script never references it
   except in section D, which is guarded by COL_LENGTH.
   ============================================================================================ */
SET NOCOUNT ON;

;WITH Waiting AS (
    SELECT b.Id, b.RequestId, b.BatchNumber, b.Status, b.CreatedAtUtc,
           r.RequestNumber,
           R1 = (SELECT MAX(h.CreatedAtUtc) FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_AREA_APPROVED'
                   AND h.Comment LIKE N'Aprovação da Área do Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' realizada%'),
           R2 = (SELECT MAX(h.CreatedAtUtc) FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_RESUBMITTED'
                   AND h.Comment LIKE N'Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' reenviado para aprovação da área%'),
           LotMentionedInTransition = CASE WHEN EXISTS (
                 SELECT 1 FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId
                   AND h.ActionTaken IN (N'BATCH_AREA_ADJUSTMENT', N'BATCH_RESUBMITTED', N'BATCH_EDITED', N'BATCH_AREA_APPROVED', N'BATCH_AREA_REJECTED', N'BATCH_FINAL_ADJUSTMENT', N'BATCH_FINAL_APPROVED', N'BATCH_FINAL_REJECTED')
                   AND h.Comment LIKE N'%Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N'[^0-9]%') THEN 1 ELSE 0 END
    FROM dbo.ApprovalBatches b
    JOIN dbo.Requests r ON r.Id = b.RequestId
    WHERE b.Status IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL')
),
Classified AS (
    SELECT *,
           [Rule] = CASE
                    WHEN Status = N'WAITING_FINAL_APPROVAL' AND R1 IS NOT NULL THEN 'R1'
                    WHEN Status = N'WAITING_AREA_APPROVAL'  AND R2 IS NOT NULL THEN 'R2'
                    WHEN Status = N'WAITING_AREA_APPROVAL'  AND LotMentionedInTransition = 0 THEN 'R3'
                    ELSE 'R0_UNESTABLISHED'
                  END,
           ProposedStageEnteredAtUtc = CASE
                    WHEN Status = N'WAITING_FINAL_APPROVAL' AND R1 IS NOT NULL THEN R1
                    WHEN Status = N'WAITING_AREA_APPROVAL'  AND R2 IS NOT NULL THEN R2
                    WHEN Status = N'WAITING_AREA_APPROVAL'  AND LotMentionedInTransition = 0 THEN CreatedAtUtc
                    ELSE NULL
                  END
    FROM Waiting
)
SELECT 'A_SUMMARY' AS Section, Status, [Rule], COUNT(*) AS Batches
FROM Classified
GROUP BY Status, [Rule]
ORDER BY Status, [Rule];

;WITH Waiting AS (
    SELECT b.Id, b.RequestId, b.BatchNumber, b.Status, b.CreatedAtUtc, r.RequestNumber,
           R1 = (SELECT MAX(h.CreatedAtUtc) FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_AREA_APPROVED'
                   AND h.Comment LIKE N'Aprovação da Área do Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' realizada%'),
           R2 = (SELECT MAX(h.CreatedAtUtc) FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_RESUBMITTED'
                   AND h.Comment LIKE N'Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' reenviado para aprovação da área%'),
           LotMentionedInTransition = CASE WHEN EXISTS (
                 SELECT 1 FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId
                   AND h.ActionTaken IN (N'BATCH_AREA_ADJUSTMENT', N'BATCH_RESUBMITTED', N'BATCH_EDITED', N'BATCH_AREA_APPROVED', N'BATCH_AREA_REJECTED', N'BATCH_FINAL_ADJUSTMENT', N'BATCH_FINAL_APPROVED', N'BATCH_FINAL_REJECTED')
                   AND h.Comment LIKE N'%Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N'[^0-9]%') THEN 1 ELSE 0 END
    FROM dbo.ApprovalBatches b
    JOIN dbo.Requests r ON r.Id = b.RequestId
    WHERE b.Status IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL')
)
SELECT 'B_WILL_BACKFILL' AS Section, RequestNumber, BatchNumber, Status,
       [Rule] = CASE WHEN Status = N'WAITING_FINAL_APPROVAL' AND R1 IS NOT NULL THEN 'R1'
                   WHEN Status = N'WAITING_AREA_APPROVAL'  AND R2 IS NOT NULL THEN 'R2' ELSE 'R3' END,
       ProposedStageEnteredAtUtc = COALESCE(CASE WHEN Status = N'WAITING_FINAL_APPROVAL' THEN R1 END,
                                            CASE WHEN Status = N'WAITING_AREA_APPROVAL' THEN R2 END,
                                            CreatedAtUtc),
       BatchCreatedAtUtc = CreatedAtUtc
FROM Waiting
WHERE (Status = N'WAITING_FINAL_APPROVAL' AND R1 IS NOT NULL)
   OR (Status = N'WAITING_AREA_APPROVAL' AND (R2 IS NOT NULL OR LotMentionedInTransition = 0))
ORDER BY ProposedStageEnteredAtUtc;

-- C: batches that stay NULL (no reliable evidence) — with the history rows that mention the lot, for manual review
;WITH Unestablished AS (
    SELECT b.Id, b.RequestId, b.BatchNumber, b.Status, b.CreatedAtUtc, r.RequestNumber
    FROM dbo.ApprovalBatches b
    JOIN dbo.Requests r ON r.Id = b.RequestId
    WHERE b.Status IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL')
      AND NOT (b.Status = N'WAITING_FINAL_APPROVAL' AND EXISTS (
                 SELECT 1 FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_AREA_APPROVED'
                   AND h.Comment LIKE N'Aprovação da Área do Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' realizada%'))
      AND NOT (b.Status = N'WAITING_AREA_APPROVAL' AND EXISTS (
                 SELECT 1 FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId AND h.ActionTaken = N'BATCH_RESUBMITTED'
                   AND h.Comment LIKE N'Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N' reenviado para aprovação da área%'))
      AND NOT (b.Status = N'WAITING_AREA_APPROVAL' AND NOT EXISTS (
                 SELECT 1 FROM dbo.RequestStatusHistories h
                 WHERE h.RequestId = b.RequestId
                   AND h.ActionTaken IN (N'BATCH_AREA_ADJUSTMENT', N'BATCH_RESUBMITTED', N'BATCH_EDITED', N'BATCH_AREA_APPROVED', N'BATCH_AREA_REJECTED', N'BATCH_FINAL_ADJUSTMENT', N'BATCH_FINAL_APPROVED', N'BATCH_FINAL_REJECTED')
                   AND h.Comment LIKE N'%Lote #' + CAST(b.BatchNumber AS nvarchar(10)) + N'[^0-9]%'))
)
SELECT 'C_UNESTABLISHED' AS Section, u.RequestNumber, u.BatchNumber, u.Status, u.CreatedAtUtc AS BatchCreatedAtUtc,
       h.ActionTaken, h.CreatedAtUtc AS HistoryAtUtc, LEFT(h.Comment, 160) AS CommentHead
FROM Unestablished u
LEFT JOIN dbo.RequestStatusHistories h
       ON h.RequestId = u.RequestId
      AND h.Comment LIKE N'%Lote #' + CAST(u.BatchNumber AS nvarchar(10)) + N'[^0-9]%'
ORDER BY u.RequestNumber, u.BatchNumber, h.CreatedAtUtc;

-- D (post-migration only): current column state
IF COL_LENGTH('dbo.ApprovalBatches', 'StageEnteredAtUtc') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        SELECT ''D_POST_MIGRATION'' AS Section, Status,
               Established = SUM(CASE WHEN StageEnteredAtUtc IS NOT NULL THEN 1 ELSE 0 END),
               StillNull   = SUM(CASE WHEN StageEnteredAtUtc IS NULL THEN 1 ELSE 0 END)
        FROM dbo.ApprovalBatches
        WHERE Status IN (N''WAITING_AREA_APPROVAL'', N''WAITING_FINAL_APPROVAL'')
        GROUP BY Status;';
END
ELSE
    SELECT 'D_POST_MIGRATION' AS Section, 'Column StageEnteredAtUtc not present yet (pre-migration run).' AS Note;
