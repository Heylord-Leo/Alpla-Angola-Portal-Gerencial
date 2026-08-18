/* =============================================================================
   Alpla Angola - Portal Gerencial
   POST-PAYMENT COMPLETION WORKFLOW - RELEASE 1 READ-ONLY AUDIT REPORT
   =============================================================================

   *** STRICTLY READ-ONLY. THIS SCRIPT CONTAINS NO DML AND NO DDL. ***

   It performs SELECT statements only. There is no INSERT, UPDATE, DELETE,
   MERGE, TRUNCATE, ALTER, DROP or sp_rename anywhere in this file, and there is
   deliberately no @Mode = 'APPLY' switch to enable one. Running it cannot change
   a single row in any environment.

   Purpose (plan v6 §22.1 - "audit report first"):
   Establish the factual baseline BEFORE any decision is taken about historical
   data. Releases 2-5 must be planned against measured reality, not assumptions.
   In particular it answers:

     1. How many legacy RECEIPT attachments exist, and on which requests?
        Legacy RECEIPT is semantically ambiguous (fiscal receipt vs. supplier
        delivery note) and is NEVER renamed or reclassified automatically
        (rule R18). This report only counts and locates them.

     2. How many PO groups would be UNCLASSIFIED once the workflow is enabled,
        split between open and completed parents? Open grouped requests must be
        classified by Finance (Release 5) before they can complete.

     3. Which open requests have NO PO group at all? These are the only requests
        for which the legacy FinalizeRequest fallback stays permitted once the
        feature is enabled (plan v6 §22.5).

     4. Are there requests that would be immediately completable, or would need
        a Fiscal Receipt, once the workflow is activated? (Sizing only.)

   Prerequisites: migration 20260730155156_AddPostPaymentDimensions applied.
   Sections 2 and 4 read the new columns; sections 1, 3 and 5 do not.

   Where to run: DEV clone first, then TEST, then (read-only) PROD.
   RULE_DEV_DATABASE applies - verify DB_NAME() before running locally.
   ============================================================================= */

SET NOCOUNT ON;

PRINT '=== Post-Payment Completion - Release 1 read-only audit ===';
PRINT 'Database : ' + DB_NAME();
PRINT 'Server   : ' + @@SERVERNAME;
PRINT 'Run at   : ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126) + ' UTC';
PRINT '';


/* -----------------------------------------------------------------------------
   SECTION 1 - Legacy RECEIPT attachments
   Nothing here is renamed. This is an inventory, not a migration.
   ----------------------------------------------------------------------------- */

PRINT '--- 1a. RECEIPT attachment totals by parent request status ---';

SELECT
    ParentStatus        = rs.Code,
    ReceiptAttachments  = COUNT(*),
    DistinctRequests    = COUNT(DISTINCT a.RequestId),
    Voided              = SUM(CASE WHEN a.VoidedAtUtc IS NOT NULL THEN 1 ELSE 0 END),
    SoftDeleted         = SUM(CASE WHEN a.IsDeleted = 1 THEN 1 ELSE 0 END),
    EarliestUploadUtc   = MIN(a.UploadedAtUtc),
    LatestUploadUtc     = MAX(a.UploadedAtUtc)
FROM dbo.RequestAttachments a
INNER JOIN dbo.Requests r  ON r.Id = a.RequestId
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE a.AttachmentTypeCode = 'RECEIPT'
GROUP BY rs.Code
ORDER BY COUNT(*) DESC;

PRINT '';
PRINT '--- 1b. RECEIPT attachments on OPEN requests (need Finance review, never auto-converted) ---';

SELECT
    r.RequestNumber,
    RequestStatus     = rs.Code,
    RequestType       = rt.Code,
    AttachmentId      = a.Id,
    a.FileName,
    a.UploadedAtUtc,
    IsVoided          = CASE WHEN a.VoidedAtUtc IS NOT NULL THEN 1 ELSE 0 END,
    a.IsDeleted,
    LinkedToPoGroup   = CASE WHEN a.RequestPoGroupId IS NULL THEN 0 ELSE 1 END
FROM dbo.RequestAttachments a
INNER JOIN dbo.Requests r        ON r.Id = a.RequestId
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
INNER JOIN dbo.RequestTypes rt   ON rt.Id = r.RequestTypeId
WHERE a.AttachmentTypeCode = 'RECEIPT'
  AND rs.Code NOT IN ('COMPLETED', 'CANCELLED', 'REJECTED')
ORDER BY a.UploadedAtUtc DESC;

PRINT '';
PRINT '--- 1c. Sanity check: no FINAL_INVOICE / FISCAL_RECEIPT attachment should exist before Release 3/4 ---';

SELECT
    a.AttachmentTypeCode,
    AttachmentCount = COUNT(*)
FROM dbo.RequestAttachments a
WHERE a.AttachmentTypeCode IN ('FINAL_INVOICE', 'FISCAL_RECEIPT')
GROUP BY a.AttachmentTypeCode;
-- Expected after Release 1: zero rows.


/* -----------------------------------------------------------------------------
   SECTION 2 - Classification exposure (requires the Release 1 migration)
   ----------------------------------------------------------------------------- */

PRINT '';
PRINT '--- 2a. PO groups by FinalInvoiceStatus and parent-request state ---';

SELECT
    g.FinalInvoiceStatus,
    ParentState = CASE
                     WHEN rs.Code = 'COMPLETED' THEN 'COMPLETED (legacy-completed, no action)'
                     WHEN rs.Code = 'CANCELLED' THEN 'CANCELLED (no action)'
                     ELSE 'OPEN (classification required before completion)'
                  END,
    GroupCount       = COUNT(*),
    DistinctRequests = COUNT(DISTINCT g.RequestId)
FROM dbo.RequestPoGroups g
INNER JOIN dbo.Requests r         ON r.Id = g.RequestId
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
GROUP BY g.FinalInvoiceStatus,
         CASE
             WHEN rs.Code = 'COMPLETED' THEN 'COMPLETED (legacy-completed, no action)'
             WHEN rs.Code = 'CANCELLED' THEN 'CANCELLED (no action)'
             ELSE 'OPEN (classification required before completion)'
         END
ORDER BY 1, 2;
-- Expected after Release 1: every row is UNCLASSIFIED (no code assigns anything else yet).

PRINT '';
PRINT '--- 2b. OPEN requests whose groups are UNCLASSIFIED (the Release 5 classification backlog) ---';

SELECT
    r.RequestNumber,
    RequestStatus       = rs.Code,
    RequestType         = rt.Code,
    r.CreatedAtUtc,
    UnclassifiedGroups  = COUNT(*),
    TotalActiveGroups   = SUM(CASE WHEN g.Status <> 'CANCELLED' THEN 1 ELSE 0 END)
FROM dbo.RequestPoGroups g
INNER JOIN dbo.Requests r         ON r.Id = g.RequestId
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
INNER JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
WHERE g.FinalInvoiceStatus = 'UNCLASSIFIED'
  AND g.Status <> 'CANCELLED'
  AND rs.Code NOT IN ('COMPLETED', 'CANCELLED', 'REJECTED')
GROUP BY r.RequestNumber, rs.Code, rt.Code, r.CreatedAtUtc
ORDER BY r.CreatedAtUtc DESC;


/* -----------------------------------------------------------------------------
   SECTION 3 - Groupless requests (the only legacy FinalizeRequest fallback)
   ----------------------------------------------------------------------------- */

PRINT '';
PRINT '--- 3a. Open requests with NO PO group at all ---';

SELECT
    r.RequestNumber,
    RequestStatus = rs.Code,
    RequestType   = rt.Code,
    r.CreatedAtUtc,
    r.SubmittedAtUtc
FROM dbo.Requests r
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
INNER JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
WHERE rs.Code NOT IN ('COMPLETED', 'CANCELLED', 'REJECTED', 'DRAFT')
  AND NOT EXISTS (SELECT 1 FROM dbo.RequestPoGroups g WHERE g.RequestId = r.Id)
ORDER BY r.CreatedAtUtc DESC;

PRINT '';
PRINT '--- 3b. Groupless summary by status (sizing for the transitional fallback) ---';

SELECT
    RequestStatus = rs.Code,
    RequestType   = rt.Code,
    RequestCount  = COUNT(*)
FROM dbo.Requests r
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
INNER JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
WHERE NOT EXISTS (SELECT 1 FROM dbo.RequestPoGroups g WHERE g.RequestId = r.Id)
GROUP BY rs.Code, rt.Code
ORDER BY COUNT(*) DESC;


/* -----------------------------------------------------------------------------
   SECTION 4 - Activation sizing (informational only)
   ----------------------------------------------------------------------------- */

PRINT '';
PRINT '--- 4a. Requests currently in WAITING_RECEIPT (today finalized by the legacy endpoint) ---';

SELECT
    r.RequestNumber,
    RequestType      = rt.Code,
    r.CreatedAtUtc,
    ActiveGroups     = (SELECT COUNT(*) FROM dbo.RequestPoGroups g
                        WHERE g.RequestId = r.Id AND g.Status <> 'CANCELLED'),
    HasLegacyReceipt = CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.RequestAttachments a
                            WHERE a.RequestId = r.Id
                              AND a.AttachmentTypeCode = 'RECEIPT'
                              AND a.IsDeleted = 0
                              AND a.VoidedAtUtc IS NULL)
                       THEN 1 ELSE 0 END
FROM dbo.Requests r
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
INNER JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
WHERE rs.Code = 'WAITING_RECEIPT'
ORDER BY r.CreatedAtUtc DESC;

PRINT '';
PRINT '--- 4b. Age distribution of open grouped requests vs. a candidate effective date ---';
-- Adjust @CandidateEffectiveDate when Release 5 proposes one. Read-only either way.

DECLARE @CandidateEffectiveDate datetime2(7) = '2026-08-15T00:00:00';

SELECT
    Cohort = CASE WHEN r.CreatedAtUtc >= @CandidateEffectiveDate
                  THEN 'New workflow mandatory (classified at creation)'
                  ELSE 'Historical compatibility (classification still required before completion)'
             END,
    RequestCount = COUNT(DISTINCT r.Id),
    GroupCount   = COUNT(*)
FROM dbo.RequestPoGroups g
INNER JOIN dbo.Requests r         ON r.Id = g.RequestId
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Status <> 'CANCELLED'
  AND rs.Code NOT IN ('COMPLETED', 'CANCELLED', 'REJECTED')
GROUP BY CASE WHEN r.CreatedAtUtc >= @CandidateEffectiveDate
              THEN 'New workflow mandatory (classified at creation)'
              ELSE 'Historical compatibility (classification still required before completion)'
         END;


/* -----------------------------------------------------------------------------
   SECTION 5 - Completed requests remain untouched (LEGACY_COMPLETED evidence)
   ----------------------------------------------------------------------------- */

PRINT '';
PRINT '--- 5a. Completed requests: counted, never reconstructed ---';

SELECT
    CompletedRequests   = COUNT(*),
    WithLegacyReceipt   = SUM(CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.RequestAttachments a
                                WHERE a.RequestId = r.Id AND a.AttachmentTypeCode = 'RECEIPT')
                              THEN 1 ELSE 0 END),
    WithoutAnyReceipt   = SUM(CASE WHEN NOT EXISTS (
                                SELECT 1 FROM dbo.RequestAttachments a
                                WHERE a.RequestId = r.Id AND a.AttachmentTypeCode = 'RECEIPT')
                              THEN 1 ELSE 0 END)
FROM dbo.Requests r
INNER JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE rs.Code = 'COMPLETED';
-- No action follows from this section. Completed requests stay LEGACY_COMPLETED (rule R16):
-- no backfill, no classification, no history reconstruction, in any release.

PRINT '';
PRINT '=== End of read-only audit ===';
