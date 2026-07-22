/* =============================================================================
   Alpla Angola - Portal Gerencial
   ROLLBACK SCRIPT - REVIEW ONLY - NOT EXECUTED
   =============================================================================

   *** THIS SCRIPT HAS NOT BEEN RUN AGAINST ANY DATABASE. ***
   Default @Mode is 'PREVIEW'. Setting @Mode = 'APPLY' requires an explicit edit
   to this script and is NOT authorized as part of this change.

   Companion to remediate-legacy-po-group-status-payment-po-issued.sql. Targets
   the exact same 12 GroupIds, no placeholders. Restores
   RequestPoGroups.Status = 'PENDING' and RequestPoGroups.UpdatedAtUtc = NULL -
   i.e. exactly reversing the remediation script's only two column changes.
   Touches no other column, table, or row.

   PROVENANCE SAFEGUARD (mandatory): Status = 'PO_ISSUED' alone does not prove
   this specific remediation execution produced that state - a row could
   independently reach PO_ISSUED through a different, later, legitimate
   operation (e.g. a correct future PO registration on a row already fixed
   another way) before this rollback runs. Rolling that row back to PENDING
   would then be wrong. This script therefore REQUIRES
   @ExpectedRemediationUpdatedAtUtc to be set to the exact UpdatedAtUtc value
   the remediation script's own final audit SELECT reported (identical across
   all 12 rows, since the remediation script computes @Now once and reuses it).
   A row is only eligible for rollback if its LIVE RequestPoGroups.UpdatedAtUtc
   still matches that exact value - if it does not (including if it is NULL,
   meaning something else touched the row without setting a timestamp at all),
   the row is refused, never silently rolled back. If
   @ExpectedRemediationUpdatedAtUtc is left NULL, @Mode = 'APPLY' is
   hard-blocked before any transaction is opened.

   Safety-critical difference from a naive "undo": beyond the timestamp
   safeguard above, this script also checks for evidence that Finance may have
   already acted on the corrected state:
     1. Current RequestPoGroups.Status must be exactly 'PO_ISSUED'.
     2. Current RequestPoGroups.UpdatedAtUtc must exactly equal
        @ExpectedRemediationUpdatedAtUtc (the provenance safeguard above).
     3. No RequestPayment row may exist for the request (a schedule/payment
        would mean Finance already used the corrected state).
     4. Parent Request.Status.Code must still be 'PO_ISSUED' (unchanged since
        the remediation ran).
   If ANY of the 12 rows fails any of these checks, the ENTIRE rollback aborts
   with zero changes (all-or-nothing, matching the remediation script).

   No credentials or connection strings are embedded in this script.
   ============================================================================= */

SET NOCOUNT ON;

DECLARE @Mode nvarchar(10) = N'PREVIEW';  -- 'PREVIEW' or 'APPLY' — APPLY is NOT authorized yet.

-- MANDATORY — set this to the exact AfterUpdatedAtUtc value the remediation
-- script's own final audit SELECT reported (identical across all 12 rows).
-- Leave NULL to preview safely; @Mode = 'APPLY' is refused while this is NULL.
DECLARE @ExpectedRemediationUpdatedAtUtc datetime2 = NULL;

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
    e.RequestNumber,
    r.Id                              AS RequestId,
    e.GroupId,
    rs.Code                          AS ParentStatusCode,
    g.Status                          AS CurrentGroupStatus,
    N'PENDING'                        AS ProposedGroupStatus,
    g.UpdatedAtUtc                    AS CurrentUpdatedAtUtc,
    @ExpectedRemediationUpdatedAtUtc  AS ExpectedRemediationUpdatedAtUtc,
    (SELECT COUNT(*) FROM dbo.RequestPayments p WHERE p.RequestId = r.Id) AS RequestPaymentCount,
    CASE
        WHEN @ExpectedRemediationUpdatedAtUtc IS NULL
            THEN 'FAIL: @ExpectedRemediationUpdatedAtUtc parameter is not set - required for provenance verification before rollback can proceed'
        WHEN g.Id IS NULL THEN 'FAIL: GroupId not found'
        WHEN g.Status <> N'PO_ISSUED' THEN 'FAIL: current status is not PO_ISSUED - rollback not applicable'
        WHEN g.UpdatedAtUtc IS NULL OR g.UpdatedAtUtc <> @ExpectedRemediationUpdatedAtUtc
            THEN 'FAIL: current UpdatedAtUtc does not match the expected remediation timestamp - this PO_ISSUED state may have been produced by a different operation, refusing rollback'
        WHEN (SELECT COUNT(*) FROM dbo.RequestPayments p WHERE p.RequestId = r.Id) > 0
            THEN 'FAIL: RequestPayment row(s) now exist - Finance may have acted on the corrected state, refusing rollback'
        WHEN rs.Code <> N'PO_ISSUED' THEN 'FAIL: parent Request.Status has changed since remediation, refusing rollback'
        ELSE 'SAFE_TO_ROLLBACK'
    END AS FinalClassification
FROM ExpectedRows e
LEFT JOIN dbo.RequestPoGroups g  ON g.Id = e.GroupId
LEFT JOIN dbo.Requests r         ON r.Id = g.RequestId
LEFT JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
ORDER BY e.RequestNumber;

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
    PRINT 'PREVIEW MODE - no UPDATE was issued. Review FinalClassification for all 12 rows above; every row must read SAFE_TO_ROLLBACK before setting @Mode = ''APPLY''.';
    RETURN;
END

-- @Mode = 'APPLY' from here on. Hard-block if the provenance parameter was never set.
IF @ExpectedRemediationUpdatedAtUtc IS NULL
BEGIN
    RAISERROR(N'ABORT: @ExpectedRemediationUpdatedAtUtc is NULL. This mandatory provenance parameter must be set to the exact AfterUpdatedAtUtc value reported by the remediation script before APPLY is allowed. No changes made.', 16, 1);
    RETURN;
END

/* =============================================================================
   APPLY - only reached when @Mode = 'APPLY' AND the provenance parameter is
   set. NOT AUTHORIZED TO RUN AS PART OF THIS CHANGE.
   ============================================================================= */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

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
    g.Status        = N'PENDING',
    g.UpdatedAtUtc  = NULL
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
JOIN dbo.RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Status = N'PO_ISSUED'
  AND g.UpdatedAtUtc = @ExpectedRemediationUpdatedAtUtc
  AND rs.Code = N'PO_ISSUED'
  AND (SELECT COUNT(*) FROM dbo.RequestPayments p WHERE p.RequestId = r.Id) = 0;

IF @@ROWCOUNT <> 12
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, N'ABORT: expected exactly 12 rows to be rolled back; found a different count (one or more rows may not match the expected remediation timestamp or no longer match the safe-rollback preconditions). No changes made.', 1;
END

COMMIT TRANSACTION;

-- Final audit display (ordinary read-only SELECT, runs AFTER COMMIT).
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

PRINT 'SUCCESS: exactly 12 rows rolled back to PENDING (UpdatedAtUtc cleared) and committed. See the result set above for the before/after audit trail.';
