/* =============================================================================
   Alpla Angola - Portal Gerencial
   REMEDIATION SCRIPT - REVIEW ONLY - NOT EXECUTED AS PART OF THIS CHANGE
   =============================================================================

   *** THIS SCRIPT HAS NOT BEEN RUN AGAINST ANY DATABASE (DEV, TEST, OR PROD). ***
   Default @Mode is 'PREVIEW'. Setting @Mode = 'APPLY' requires an explicit edit
   to this script and is NOT authorized as part of this change — do not run
   APPLY anywhere without separate, explicit approval.

   Root cause addressed (Finance > Payments investigation, 2026-07-22):
   12 PAYMENT-type RequestPoGroup rows have Status = 'PENDING' even though their
   parent Request.Status.Code has already advanced to 'PO_ISSUED'. Because
   'PENDING' is not a finance-pipeline group status, FinanceController.GetPayments
   returned an empty PoGroups array for these rows, and the frontend hid
   "Agendar pagamento" / "Marcar como pago" even though the backend's own
   eligibility rule (IFinancePaymentEligibilityService) considers them available.
   All 12 rows share RequestPoGroup.CreatedAtUtc = 2026-07-20 17:25:39 — a single
   historical backfill batch, confirmed via read-only investigation, not a
   currently-active code bug (RegisterPo already keeps both fields in sync for
   anything registered through it today).

   Scope discipline: this is the smallest safe correction proportional to the
   confirmed issue. It updates ONLY:
     - RequestPoGroups.Status:       PENDING -> PO_ISSUED
     - RequestPoGroups.UpdatedAtUtc: one single, consistent UTC timestamp
   It does NOT touch Request.Status, RequestPayments, attachments, Supplier,
   or any other column/table. It does NOT insert a RequestStatusHistory row
   (the parent Request.Status is already correctly PO_ISSUED — this script only
   corrects the group's own status field to match).

   Manifest (all 12 rows, exact IDs — confirmed via read-only query against the
   local Portal-Gerencial-Dev-ProdClone database on 2026-07-22; the same query,
   or the one embedded as the "independent cohort check" below, should be run
   again against TEST/PROD before relying on this manifest there):

     REQ-13/07/2026-053  GroupId E5191503-54D5-4D7B-93BA-DAE34378B279
     REQ-13/07/2026-054  GroupId 76CC4600-D295-4641-A686-B7499C6C31B5
     REQ-13/07/2026-055  GroupId E7A8D913-74DA-480B-8AC3-A35F979F0010
     REQ-14/07/2026-056  GroupId 9706EAB4-DF18-4870-AA50-C14AF3B1D7D2
     REQ-14/07/2026-057  GroupId 1ACCA13E-E9A2-41A1-A905-B88CCD5E2EF1
     REQ-14/07/2026-059  GroupId 69AE5D7F-5839-4FEA-829E-DE36173DA424
     REQ-14/07/2026-061  GroupId 945B6919-291B-4F59-85B8-4CC0AC61F34B
     REQ-14/07/2026-063  GroupId 2C44E922-A10B-4C3A-AD43-311D24F8A183
     REQ-14/07/2026-064  GroupId 6DB4D1E3-6827-4925-8605-751D1AAD274E
     REQ-14/07/2026-065  GroupId 304FBFC8-8710-4809-9A63-3AA4E4A739E3
     REQ-14/07/2026-066  GroupId 1913079B-87EB-42DE-A7BE-31A07B0E80FE
     REQ-15/07/2026-077  GroupId 37C09177-51B0-4F60-93F0-FB7D602C2CDF

   Preconditions re-validated LIVE, inside the transaction, immediately before
   the UPDATE (all must hold for ALL 12 rows or the entire operation aborts with
   zero changes - all-or-nothing):
     1. RequestType.Code = 'PAYMENT'
     2. Request.Status.Code = 'PO_ISSUED'
     3. RequestPoGroup.Status = 'PENDING'
     4. RequestPoGroup.CreatedAtUtc = '2026-07-20 17:25:39.0633333' (the confirmed batch fingerprint)
     5. No row outside the 12-row manifest currently matches predicates 1-3 above
        (an independent cohort re-check - if a 13th row now matches, or one of
        the 12 no longer does, the script aborts rather than silently acting on
        a changed cohort)

   SQL Server compatibility note (verified in this engagement, not assumed): the
   OUTPUT clause of an UPDATE statement can reference ONLY inserted/deleted
   pseudo-columns (columns of the table being updated) - it CANNOT reference a
   table joined in via UPDATE...FROM. This script captures RequestId (a genuine
   RequestPoGroups column) via OUTPUT ... INTO a table variable, then joins that
   table variable to dbo.Requests in a plain SELECT AFTER COMMIT to display
   RequestNumber - an ordinary join has no such restriction.

   Companion rollback script: rollback-legacy-po-group-status-payment-po-issued.sql
   (requires the exact UpdatedAtUtc this script's APPLY run produces - shown in
   this script's own final audit SELECT - as a provenance safeguard, so it can
   never roll back a row that reached PO_ISSUED through a different, later,
   legitimate operation).

   No credentials or connection strings are embedded in this script. Connect
   with whatever account your DBA/change process requires for the target
   environment; this script assumes the connection's current database context
   is already the correct one for that environment.
   ============================================================================= */

SET NOCOUNT ON;

DECLARE @Mode nvarchar(10) = N'PREVIEW';  -- 'PREVIEW' or 'APPLY' — APPLY is NOT authorized yet.
DECLARE @Now datetime2 = SYSUTCDATETIME(); -- single consistent timestamp, used only if @Mode = 'APPLY' proceeds to COMMIT
DECLARE @ExpectedBatchCreatedAtUtc datetime2 = '2026-07-20 17:25:39.0633333';
DECLARE @ExpectedRowCount int = 12;

;WITH ExpectedRows (RequestNumber, GroupId) AS (
    SELECT x.RequestNumber, CAST(x.GroupId AS uniqueidentifier)
    FROM (VALUES
        (N'REQ-13/07/2026-053', N'E5191503-54D5-4D7B-93BA-DAE34378B279'),
        (N'REQ-13/07/2026-054', N'76CC4600-D295-4641-A686-B7499C6C31B5'),
        (N'REQ-13/07/2026-055', N'E7A8D913-74DA-480B-8AC3-A35F979F0010'),
        (N'REQ-14/07/2026-056', N'9706EAB4-DF18-4870-AA50-C14AF3B1D7D2'),
        (N'REQ-14/07/2026-057', N'1ACCA13E-E9A2-41A1-A905-B88CCD5E2EF1'),
        (N'REQ-14/07/2026-059', N'69AE5D7F-5839-4FEA-829E-DE36173DA424'),
        (N'REQ-14/07/2026-061', N'945B6919-291B-4F59-85B8-4CC0AC61F34B'),
        (N'REQ-14/07/2026-063', N'2C44E922-A10B-4C3A-AD43-311D24F8A183'),
        (N'REQ-14/07/2026-064', N'6DB4D1E3-6827-4925-8605-751D1AAD274E'),
        (N'REQ-14/07/2026-065', N'304FBFC8-8710-4809-9A63-3AA4E4A739E3'),
        (N'REQ-14/07/2026-066', N'1913079B-87EB-42DE-A7BE-31A07B0E80FE'),
        (N'REQ-15/07/2026-077', N'37C09177-51B0-4F60-93F0-FB7D602C2CDF')
    ) AS x(RequestNumber, GroupId)
)
SELECT
    e.RequestNumber                    AS ExpectedRequestNumber,
    r.Id                                AS RequestId,
    e.GroupId,
    rt.Code                             AS RequestTypeCode,
    rs.Code                             AS ParentStatusCode,
    g.Status                            AS CurrentGroupStatus,
    N'PO_ISSUED'                        AS ProposedGroupStatus,
    g.CreatedAtUtc,
    g.TotalAmount,
    g.CurrencyCode,
    CASE WHEN r.RequestNumber = e.RequestNumber THEN 1 ELSE 0 END AS Check_RequestNumberMatches,
    CASE WHEN rt.Code = 'PAYMENT' THEN 1 ELSE 0 END AS Check_IsPaymentType,
    CASE WHEN rs.Code = 'PO_ISSUED' THEN 1 ELSE 0 END AS Check_ParentIsPoIssued,
    CASE WHEN g.Status = 'PENDING' THEN 1 ELSE 0 END AS Check_GroupIsPending,
    CASE WHEN g.CreatedAtUtc = @ExpectedBatchCreatedAtUtc THEN 1 ELSE 0 END AS Check_MatchesBatchFingerprint,
    CASE
        WHEN g.Id IS NULL THEN 'FAIL: GroupId not found in live database'
        WHEN r.RequestNumber <> e.RequestNumber THEN 'FAIL: RequestNumber mismatch for this GroupId'
        WHEN rt.Code <> 'PAYMENT' THEN 'FAIL: request is not PAYMENT type'
        WHEN rs.Code <> 'PO_ISSUED' THEN 'FAIL: parent Request.Status is not PO_ISSUED'
        WHEN g.Status = 'PO_ISSUED' THEN 'ALREADY_REMEDIATED'
        WHEN g.Status <> 'PENDING' THEN 'FAIL: group status is neither PENDING nor PO_ISSUED - needs individual review'
        WHEN g.CreatedAtUtc <> @ExpectedBatchCreatedAtUtc THEN 'FAIL: CreatedAtUtc does not match the confirmed batch fingerprint'
        ELSE 'READY'
    END AS FinalClassification
FROM ExpectedRows e
LEFT JOIN dbo.RequestPoGroups g  ON g.Id = e.GroupId
LEFT JOIN dbo.Requests r         ON r.Id = g.RequestId
LEFT JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
LEFT JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
ORDER BY e.RequestNumber;

-- Independent cohort re-check: every row in the LIVE database currently matching the same
-- predicate the original investigation used, regardless of the manifest above. If this returns
-- a different count than @ExpectedRowCount, the cohort has changed since the manifest was built
-- and APPLY must be refused until investigated (see Check_CohortRowCountMatches below).
PRINT 'Independent cohort re-check (live PAYMENT + PO_ISSUED + PENDING rows, any CreatedAtUtc):';
SELECT
    COUNT(*) AS LiveCohortRowCount,
    @ExpectedRowCount AS ExpectedRowCount,
    CASE WHEN COUNT(*) = @ExpectedRowCount THEN 'MATCH' ELSE 'MISMATCH - DO NOT APPLY, INVESTIGATE FIRST' END AS Check_CohortRowCountMatches
FROM dbo.RequestPoGroups g
JOIN dbo.Requests r ON r.Id = g.RequestId
JOIN dbo.RequestTypes rt ON rt.Id = r.RequestTypeId
JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE rt.Code = 'PAYMENT' AND rs.Code = 'PO_ISSUED' AND g.Status = 'PENDING';

/* =============================================================================
   MODE GATE
   ============================================================================= */
IF @Mode NOT IN (N'PREVIEW', N'APPLY')
BEGIN
    RAISERROR(N'ABORT: @Mode must be exactly ''PREVIEW'' or ''APPLY''.', 16, 1);
    RETURN;
END

IF @Mode = N'PREVIEW'
BEGIN
    PRINT 'PREVIEW MODE - no UPDATE was issued. Review FinalClassification for all 12 rows above (every row must read READY) AND confirm Check_CohortRowCountMatches = MATCH before ever considering @Mode = ''APPLY''.';
    RETURN;
END

/* =============================================================================
   APPLY - only reached when @Mode = 'APPLY'. Preconditions are revalidated live,
   inside this transaction, via the UPDATE statement's own WHERE clause (not
   merely re-displayed above) - this is the actual enforcement, not just the
   preview. NOT AUTHORIZED TO RUN AS PART OF THIS CHANGE.
   ============================================================================= */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Re-check the independent cohort count inside the transaction too, so a row that changed
-- between the preview above and this APPLY execution still aborts the whole operation.
DECLARE @LiveCohortRowCount int = (
    SELECT COUNT(*)
    FROM dbo.RequestPoGroups g
    JOIN dbo.Requests r ON r.Id = g.RequestId
    JOIN dbo.RequestTypes rt ON rt.Id = r.RequestTypeId
    JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
    WHERE rt.Code = 'PAYMENT' AND rs.Code = 'PO_ISSUED' AND g.Status = 'PENDING'
);
IF @LiveCohortRowCount <> @ExpectedRowCount
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, N'ABORT: live cohort row count does not match the expected manifest size. No changes made. Investigate before retrying.', 1;
END

DECLARE @AuditResult TABLE (
    RequestId           uniqueidentifier,
    GroupId              uniqueidentifier,
    BeforeStatus         nvarchar(50),
    AfterStatus          nvarchar(50),
    BeforeUpdatedAtUtc   datetime2,
    AfterUpdatedAtUtc    datetime2
);

;WITH ExpectedRows (RequestNumber, GroupId) AS (
    SELECT x.RequestNumber, CAST(x.GroupId AS uniqueidentifier)
    FROM (VALUES
        (N'REQ-13/07/2026-053', N'E5191503-54D5-4D7B-93BA-DAE34378B279'),
        (N'REQ-13/07/2026-054', N'76CC4600-D295-4641-A686-B7499C6C31B5'),
        (N'REQ-13/07/2026-055', N'E7A8D913-74DA-480B-8AC3-A35F979F0010'),
        (N'REQ-14/07/2026-056', N'9706EAB4-DF18-4870-AA50-C14AF3B1D7D2'),
        (N'REQ-14/07/2026-057', N'1ACCA13E-E9A2-41A1-A905-B88CCD5E2EF1'),
        (N'REQ-14/07/2026-059', N'69AE5D7F-5839-4FEA-829E-DE36173DA424'),
        (N'REQ-14/07/2026-061', N'945B6919-291B-4F59-85B8-4CC0AC61F34B'),
        (N'REQ-14/07/2026-063', N'2C44E922-A10B-4C3A-AD43-311D24F8A183'),
        (N'REQ-14/07/2026-064', N'6DB4D1E3-6827-4925-8605-751D1AAD274E'),
        (N'REQ-14/07/2026-065', N'304FBFC8-8710-4809-9A63-3AA4E4A739E3'),
        (N'REQ-14/07/2026-066', N'1913079B-87EB-42DE-A7BE-31A07B0E80FE'),
        (N'REQ-15/07/2026-077', N'37C09177-51B0-4F60-93F0-FB7D602C2CDF')
    ) AS x(RequestNumber, GroupId)
)
UPDATE g
SET
    g.Status        = N'PO_ISSUED',
    g.UpdatedAtUtc  = @Now
OUTPUT
    inserted.RequestId,
    deleted.Id              AS GroupId,
    deleted.Status          AS BeforeStatus,
    inserted.Status         AS AfterStatus,
    deleted.UpdatedAtUtc    AS BeforeUpdatedAtUtc,
    inserted.UpdatedAtUtc   AS AfterUpdatedAtUtc
INTO @AuditResult
FROM dbo.RequestPoGroups g
JOIN ExpectedRows e         ON e.GroupId = g.Id
JOIN dbo.Requests r         ON r.Id = g.RequestId AND r.RequestNumber = e.RequestNumber
JOIN dbo.RequestTypes rt    ON rt.Id = r.RequestTypeId
JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE rt.Code = N'PAYMENT'
  AND rs.Code = N'PO_ISSUED'
  AND g.Status = N'PENDING'
  AND g.CreatedAtUtc = @ExpectedBatchCreatedAtUtc;

IF @@ROWCOUNT <> 12
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, N'ABORT: expected exactly 12 rows to be updated; rollback performed, no changes persisted. Re-run the preview and investigate before retrying.', 1;
END

-- Re-query and validate all affected rows before COMMIT: every one must now read PO_ISSUED.
IF EXISTS (SELECT 1 FROM @AuditResult WHERE AfterStatus <> N'PO_ISSUED')
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, N'ABORT: post-update validation found a row not at PO_ISSUED. Rolled back, no changes persisted.', 1;
END

COMMIT TRANSACTION;

-- Final audit display (ordinary read-only SELECT, runs AFTER COMMIT, joining the captured
-- in-memory @AuditResult to dbo.Requests to attach RequestNumber for display). The
-- AfterUpdatedAtUtc value shown here is REQUIRED by the companion rollback script's
-- @ExpectedRemediationUpdatedAtUtc parameter.
SELECT
    r.RequestNumber,
    ar.GroupId,
    ar.BeforeStatus,
    ar.AfterStatus,
    ar.BeforeUpdatedAtUtc,
    ar.AfterUpdatedAtUtc
FROM @AuditResult ar
JOIN dbo.Requests r ON r.Id = ar.RequestId
ORDER BY r.RequestNumber;

PRINT 'SUCCESS: exactly 12 rows updated (PENDING -> PO_ISSUED) and committed. See the result set above for the before/after audit trail. Record AfterUpdatedAtUtc for the rollback script.';

/* =============================================================================
   HOW TO RUN (for reference — none of these have been executed by this change)

   PREVIEW against local DEV clone (safe, read-only, default mode):
       sqlcmd -S "(localdb)\MSSQLLocalDB" -d "Portal-Gerencial-Dev-ProdClone" -E -i "scripts\db\remediate-legacy-po-group-status-payment-po-issued.sql"

   PREVIEW against TEST (safe, read-only, default mode - connect with your normal TEST credentials):
       sqlcmd -S "<TEST_SERVER>" -d "Portal-Gerencial-Test" -i "scripts\db\remediate-legacy-po-group-status-payment-po-issued.sql"

   PREVIEW against PROD (safe, read-only, default mode - connect with your normal PROD credentials):
       sqlcmd -S "<PROD_SERVER>" -d "Portal-Gerencial" -i "scripts\db\remediate-legacy-po-group-status-payment-po-issued.sql"

   APPLY (ANY environment) — NOT AUTHORIZED YET. Requires:
     1. Every row in the PREVIEW output reads FinalClassification = 'READY'.
     2. Check_CohortRowCountMatches = 'MATCH'.
     3. Explicit sign-off from whoever owns this remediation, per environment.
     4. Manually edit the @Mode declaration near the top of this script from
        'PREVIEW' to 'APPLY', then re-run the exact same command used for PREVIEW.
   ============================================================================= */
