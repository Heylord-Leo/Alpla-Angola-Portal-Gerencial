-- ============================================================================
-- ███  READ-ONLY  ███   Approval notifications — Phase 1 live state (PROD / TEST)
-- ============================================================================
-- Purpose : Establish, from the live Portal database, (A) the approvals pending
--           now with their stage-entry date and the recipients the routing
--           cascade would actually pick, (B) the email outbox health, (C) whether
--           the proforma deadline service has run, (D) SMTP presence (redacted),
--           (E) recent notification errors. SELECT only — no writes.
-- Columns verified against the schema of build 2.245.12 (migration
-- 20260909145120_AddPoResponsibleBuyerOwnershipToPoGroups): EmailOutbox,
-- DepartmentManagers, Users, Plants, Companies, ApprovalBatches,
-- ProformaDeadlineAlerts, RequestStatusHistories, AdminLogEntries.
-- Usage   : sqlcmd -S <instance> -d Portal-Gerencial -E -b -W
--                  -i notifications-phase1-readonly-checks.sql -o phase1-PROD.txt
-- Secrets : none selected (no passwords, no connection strings, no AdditionalConfig).
-- ============================================================================
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

DECLARE @db SYSNAME = DB_NAME();
IF @db NOT IN (N'Portal-Gerencial-Test', N'Portal-Gerencial')
BEGIN
    DECLARE @m0 NVARCHAR(300) = CONCAT(N'ABORTED: connected database is [', @db, N']; only TEST/PROD Portal databases are accepted.');
    THROW 50000, @m0, 1;
END;
PRINT CONCAT(N'== CONTEXT == server=', @@SERVERNAME, N' | database=', @db, N' | utc=', CONVERT(NVARCHAR(30), SYSUTCDATETIME(), 126),
             N' | compat=', (SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()));

-- ─────────────────────────────────────────────────────────────────────────────
-- A. Pending approvals (request level) with stage entry, routed recipients and
--    the notification history of the CURRENT stage only.
--    Stage entry = newest RequestStatusHistories row that moved the request INTO
--    its current StatusId (PreviousStatusId <> NewStatusId). Batch-flow entries
--    arrive as ActionTaken = 'STATUS_SYNC'.
--    Recipients follow ApprovalRoutingService: plant-specific active managers of
--    the department first; only if none, the department's global managers;
--    a manager counts only if the user is active with a non-empty email and the
--    plant (when set) is active. Final stage = Company.FinalApproverUserId.
-- ─────────────────────────────────────────────────────────────────────────────
PRINT N'== A1. Pending approvals — stage entry, routed recipients (cascade), current-stage notifications ==';
;WITH pending AS (
    SELECT r.Id, r.RequestNumber, rt.Code AS RequestType, s.Code AS Status, r.CreatedAtUtc, r.UpdatedAtUtc,
           r.DepartmentId, r.PlantId, r.CompanyId, r.AreaApproverId, r.FinalApproverId, r.StatusId
    FROM Requests r
    JOIN RequestStatuses s ON s.Id = r.StatusId
    JOIN RequestTypes rt ON rt.Id = r.RequestTypeId
    WHERE s.Code IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL') AND r.IsCancelled = 0
), stage AS (
    SELECT p.Id,
           (SELECT MAX(h.CreatedAtUtc) FROM RequestStatusHistories h
             WHERE h.RequestId = p.Id AND h.NewStatusId = p.StatusId
               AND (h.PreviousStatusId IS NULL OR h.PreviousStatusId <> h.NewStatusId)) AS StageEnteredUtc
    FROM pending p
), plantMgrs AS (
    SELECT p.Id, COUNT(*) AS N,
           STUFF((SELECT N'; ' + u.FullName + N' <' + u.Email + N'>' FROM DepartmentManagers dm JOIN Users u ON u.Id = dm.UserId
                  LEFT JOIN Plants pl ON pl.Id = dm.PlantId
                  WHERE dm.IsActive = 1 AND dm.DepartmentId = p.DepartmentId AND dm.PlantId = p.PlantId
                    AND u.IsActive = 1 AND u.Email IS NOT NULL AND u.Email <> N'' AND (pl.Id IS NULL OR pl.IsActive = 1)
                  FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Names
    FROM pending p JOIN DepartmentManagers dm ON dm.DepartmentId = p.DepartmentId AND dm.PlantId = p.PlantId AND dm.IsActive = 1
    JOIN Users u ON u.Id = dm.UserId AND u.IsActive = 1 AND u.Email IS NOT NULL AND u.Email <> N''
    LEFT JOIN Plants pl ON pl.Id = dm.PlantId
    WHERE p.PlantId IS NOT NULL AND (pl.Id IS NULL OR pl.IsActive = 1)
    GROUP BY p.Id
), globalMgrs AS (
    SELECT p.Id, COUNT(*) AS N,
           STUFF((SELECT N'; ' + u.FullName + N' <' + u.Email + N'>' FROM DepartmentManagers dm JOIN Users u ON u.Id = dm.UserId
                  WHERE dm.IsActive = 1 AND dm.DepartmentId = p.DepartmentId AND dm.PlantId IS NULL
                    AND u.IsActive = 1 AND u.Email IS NOT NULL AND u.Email <> N''
                  FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Names
    FROM pending p JOIN DepartmentManagers dm ON dm.DepartmentId = p.DepartmentId AND dm.PlantId IS NULL AND dm.IsActive = 1
    JOIN Users u ON u.Id = dm.UserId AND u.IsActive = 1 AND u.Email IS NOT NULL AND u.Email <> N''
    GROUP BY p.Id
)
SELECT p.RequestNumber, p.RequestType, p.Status,
       p.CreatedAtUtc, p.UpdatedAtUtc, st.StageEnteredUtc,
       DATEDIFF(DAY, st.StageEnteredUtc, SYSUTCDATETIME()) AS DaysInStage,
       p.DepartmentId, p.PlantId,
       CASE WHEN p.Status = N'WAITING_FINAL_APPROVAL' THEN
                 CASE WHEN fa.Id IS NULL THEN N'NONE (FinalApproverId missing/inactive/no email)' ELSE N'FINAL_APPROVER' END
            WHEN p.AreaApproverId IS NOT NULL AND aa.Id IS NOT NULL THEN N'LEGACY_NOMINEE (AreaApproverId; used by proforma alerts, NOT by the orchestrator)'
            WHEN ISNULL(pm.N, 0) > 0 THEN N'PLANT_SPECIFIC'
            WHEN ISNULL(gm.N, 0) > 0 THEN N'GLOBAL'
            ELSE N'NONE' END AS RoutedSource,
       CASE WHEN p.Status = N'WAITING_FINAL_APPROVAL' THEN fa.FullName + N' <' + fa.Email + N'>'
            WHEN ISNULL(pm.N, 0) > 0 THEN pm.Names
            WHEN ISNULL(gm.N, 0) > 0 THEN gm.Names
            ELSE NULL END AS RoutedRecipients,
       ISNULL(pm.N, 0) AS PlantSpecificMgrs, ISNULL(gm.N, 0) AS GlobalMgrs,
       (SELECT COUNT(*) FROM EmailOutbox e WHERE e.RequestId = p.Id AND e.CreatedAtUtc >= st.StageEnteredUtc
          AND e.EventCode IN (N'REQUEST_SUBMITTED', N'QUOTATION_COMPLETED', N'AREA_APPROVED', N'BATCH_RESUBMITTED_TO_AREA')) AS StageMailsQueued,
       (SELECT COUNT(*) FROM EmailOutbox e WHERE e.RequestId = p.Id AND e.CreatedAtUtc >= st.StageEnteredUtc
          AND e.EventCode IN (N'REQUEST_SUBMITTED', N'QUOTATION_COMPLETED', N'AREA_APPROVED', N'BATCH_RESUBMITTED_TO_AREA') AND e.Status = N'SENT') AS StageMailsSent,
       (SELECT COUNT(*) FROM ApprovalBatches b WHERE b.RequestId = p.Id AND b.Status IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL')) AS OpenBatches,
       (SELECT COUNT(*) FROM ProformaDeadlineAlerts a WHERE a.RequestId = p.Id AND a.SentAtUtc >= st.StageEnteredUtc) AS ProformaAlertsThisStage
FROM pending p
JOIN stage st ON st.Id = p.Id
LEFT JOIN plantMgrs pm ON pm.Id = p.Id
LEFT JOIN globalMgrs gm ON gm.Id = p.Id
LEFT JOIN Users aa ON aa.Id = p.AreaApproverId AND aa.IsActive = 1 AND aa.Email IS NOT NULL AND aa.Email <> N''
LEFT JOIN Users fa ON fa.Id = p.FinalApproverId AND fa.IsActive = 1 AND fa.Email IS NOT NULL AND fa.Email <> N''
ORDER BY st.StageEnteredUtc;

PRINT N'== A2. Open approval batches — the batch is the actionable unit in the batch flow; age = batch stage entry ==';
SELECT r.RequestNumber, b.BatchNumber, b.Status AS BatchStatus, b.CreatedAtUtc AS BatchCreatedUtc, b.UpdatedAtUtc AS BatchUpdatedUtc,
       (SELECT MAX(h.CreatedAtUtc) FROM RequestStatusHistories h WHERE h.RequestId = r.Id
          AND h.ActionTaken IN (N'BATCH_CREATED', N'BATCH_RESUBMITTED', N'BATCH_AREA_APPROVED')
          AND h.Comment LIKE N'%Lote #' + CAST(b.BatchNumber AS NVARCHAR(10)) + N'%') AS BatchStageEnteredUtc,
       (SELECT COUNT(*) FROM EmailOutbox e WHERE e.RequestId = r.Id AND e.CreatedAtUtc >= b.CreatedAtUtc
          AND e.EventCode IN (N'REQUEST_SUBMITTED', N'QUOTATION_COMPLETED', N'AREA_APPROVED', N'BATCH_RESUBMITTED_TO_AREA')) AS MailsSinceBatchCreated
FROM ApprovalBatches b JOIN Requests r ON r.Id = b.RequestId
WHERE b.Status IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL', N'AREA_ADJUSTMENT', N'FINAL_ADJUSTMENT')
ORDER BY b.CreatedAtUtc;

-- ─────────────────────────────────────────────────────────────────────────────
-- B. Outbox health
-- ─────────────────────────────────────────────────────────────────────────────
PRINT N'== B1. Outbox by status since 2026-09-07 ==';
SELECT Status, COUNT(*) AS N, MIN(CreatedAtUtc) AS First, MAX(CreatedAtUtc) AS Last, MAX(ProcessedAtUtc) AS LastProcessed
FROM EmailOutbox WHERE CreatedAtUtc >= '2026-09-07' GROUP BY Status ORDER BY Status;

PRINT N'== B2. Processor liveness: PENDING/FAILED rows older than 10 minutes mean the processor is not running ==';
SELECT COUNT(*) AS StalePendingOrRetryable, MIN(CreatedAtUtc) AS OldestStale
FROM EmailOutbox
WHERE (Status = N'PENDING' OR (Status = N'FAILED' AND RetryCount < MaxRetries)) AND CreatedAtUtc < DATEADD(MINUTE, -10, SYSUTCDATETIME());

PRINT N'== B3. Last successful send, and recent failures (error text only) ==';
SELECT (SELECT MAX(ProcessedAtUtc) FROM EmailOutbox WHERE Status = N'SENT') AS LastSentUtc;
SELECT TOP 15 CreatedAtUtc, ProcessedAtUtc, Status, EventCode, RequestNumber, RetryCount, LEFT(LastError, 180) AS LastError
FROM EmailOutbox WHERE Status IN (N'DEAD_LETTER', N'FAILED') ORDER BY CreatedAtUtc DESC;

-- ─────────────────────────────────────────────────────────────────────────────
-- C. Proforma deadline alert service
-- ─────────────────────────────────────────────────────────────────────────────
PRINT N'== C1. Has the service ever completed a cycle? (expect rows after 2026-09-07 if it runs) ==';
SELECT COUNT(*) AS Cycles, MIN(TimestampUtc) AS FirstCycle, MAX(TimestampUtc) AS LastCycle FROM AdminLogEntries WHERE EventType = N'PROFORMA_DEADLINE_CYCLE';
SELECT TOP 10 TimestampUtc, Message FROM AdminLogEntries WHERE EventType = N'PROFORMA_DEADLINE_CYCLE' ORDER BY TimestampUtc DESC;
SELECT COUNT(*) AS AlertRows, SUM(CASE WHEN EmailSent = 1 THEN 1 ELSE 0 END) AS EmailSent, MAX(SentAtUtc) AS LastAlert FROM ProformaDeadlineAlerts;

PRINT N'== C2. Requests eligible for a proforma alert right now (PAYMENT, approval stage, NeedByDateUtc set) ==';
SELECT r.RequestNumber, s.Code AS Status, r.NeedByDateUtc, DATEDIFF(DAY, SYSUTCDATETIME(), r.NeedByDateUtc) AS DaysRemaining,
       CASE WHEN DATEDIFF(DAY, SYSUTCDATETIME(), r.NeedByDateUtc) < 0 THEN N'EXPIRED' WHEN DATEDIFF(DAY, SYSUTCDATETIME(), r.NeedByDateUtc) IN (0, 1, 3) THEN N'LEVEL_DUE' ELSE N'no level today' END AS LevelToday,
       (SELECT COUNT(*) FROM ProformaDeadlineAlerts a WHERE a.RequestId = r.Id) AS AlertsRecorded
FROM Requests r JOIN RequestStatuses s ON s.Id = r.StatusId JOIN RequestTypes t ON t.Id = r.RequestTypeId
WHERE t.Code = N'PAYMENT' AND r.IsCancelled = 0 AND r.NeedByDateUtc IS NOT NULL AND s.Code IN (N'WAITING_AREA_APPROVAL', N'WAITING_FINAL_APPROVAL')
ORDER BY r.NeedByDateUtc;

-- ─────────────────────────────────────────────────────────────────────────────
-- D. SMTP presence (redacted) and provider flag
-- ─────────────────────────────────────────────────────────────────────────────
PRINT N'== D. SMTP settings (no secrets) ==';
SELECT Id, Server, Port, EnableSsl, SenderName,
       CASE WHEN SenderEmail IS NULL THEN N'NULL' ELSE RIGHT(SenderEmail, CHARINDEX('@', REVERSE(SenderEmail))) END AS SenderDomain,
       CASE WHEN EncryptedPassword IS NULL OR EncryptedPassword = N'' THEN N'none' ELSE N'set' END AS PasswordState,
       EnableSubjectPrefix, SubjectPrefixText, RedirectAllToTestRecipient,
       CASE WHEN TestRecipientEmail IS NULL OR TestRecipientEmail = N'' THEN N'none' ELSE N'set' END AS TestRecipientState,
       UpdatedAtUtc
FROM SmtpSettings;
SELECT Code, IsEnabled FROM IntegrationProviders WHERE Code = N'SMTP';

-- ─────────────────────────────────────────────────────────────────────────────
-- E. Notification errors, last 30 days
-- ─────────────────────────────────────────────────────────────────────────────
PRINT N'== E. Notification-related errors/warnings (last 30 days) ==';
SELECT EventType, Level, COUNT(*) AS N, MAX(TimestampUtc) AS Last
FROM AdminLogEntries
WHERE TimestampUtc >= DATEADD(DAY, -30, SYSUTCDATETIME())
  AND (EventType IN (N'APPROVAL_EMAIL_NO_RECIPIENT', N'SMTP_DISPATCH_FAILED', N'EMAIL_OUTBOX_DEAD_LETTER', N'EMAIL_OUTBOX_RETRY_SCHEDULED', N'EMAIL_OUTBOX_STUCK_RECOVERED')
       OR (Level = N'Error' AND Source IN (N'EmailService', N'Notification', N'WorkflowNotificationOrchestrator', N'EmailOutboxProcessor')))
GROUP BY EventType, Level ORDER BY Last DESC;

PRINT N'== PHASE 1 CHECKS COMPLETE (read-only; nothing was modified) ==';
