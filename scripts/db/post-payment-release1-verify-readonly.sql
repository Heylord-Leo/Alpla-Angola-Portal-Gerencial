/* =============================================================================
   Alpla Angola - Portal Gerencial
   POST-PAYMENT COMPLETION - RELEASE 1 POST-MIGRATION VERIFICATION (READ-ONLY)
   =============================================================================

   *** STRICTLY READ-ONLY. NO DML, NO DDL. ***

   Run this AFTER migration 20260730155156_AddPostPaymentDimensions has been
   applied to an environment (DEV clone, then TEST). It verifies Release 1
   acceptance criteria 3-9 of plan v6 §24.1.4 and prints a PASS/FAIL verdict per
   check. Every check is a SELECT.

   Expected verdict after a correct Release 1 deployment: every check PASS.
   ============================================================================= */

SET NOCOUNT ON;

PRINT '=== Post-Payment Completion - Release 1 verification ===';
PRINT 'Database : ' + DB_NAME();
PRINT 'Run at   : ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126) + ' UTC';
PRINT '';


/* --- CHECK 1: the migration is recorded exactly once --------------------- */

SELECT
    [Check]  = '1. Migration applied',
    Verdict  = CASE WHEN COUNT(*) = 1 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Rows in __EFMigrationsHistory for AddPostPaymentDimensions: ' + CAST(COUNT(*) AS varchar(10))
FROM dbo.__EFMigrationsHistory
WHERE MigrationId = '20260730155156_AddPostPaymentDimensions';


/* --- CHECK 2: every new column exists with the expected shape ------------ */

;WITH expected(TableName, ColumnName, DataType, IsNullable) AS (
    SELECT 'RequestStatusHistories', 'IdempotencyKey',                      'nvarchar',        1 UNION ALL
    SELECT 'Requests',               'BillingDocumentType',                 'nvarchar',        1 UNION ALL
    SELECT 'Requests',               'CompletionCycleId',                   'uniqueidentifier',1 UNION ALL
    SELECT 'Requests',               'RowVersion',                          'timestamp',       0 UNION ALL
    SELECT 'Quotations',             'DocumentType',                        'nvarchar',        1 UNION ALL
    SELECT 'RequestPoGroups',        'BillingDocumentType',                 'nvarchar',        1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceStatus',                  'nvarchar',        0 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceAttachmentId',            'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceUploadedAtUtc',           'datetime2',       1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceUploadedByUserId',        'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceValidatedAtUtc',          'datetime2',       1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceValidatedByUserId',       'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'FinalInvoiceRejectionReason',         'nvarchar',        1 UNION ALL
    SELECT 'RequestPoGroups',        'FiscalReceiptAttachmentId',           'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'FiscalReceiptUploadedAtUtc',          'datetime2',       1 UNION ALL
    SELECT 'RequestPoGroups',        'FiscalReceiptUploadedByUserId',       'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'OperationalReceiptCompletedAtUtc',    'datetime2',       1 UNION ALL
    SELECT 'RequestPoGroups',        'OperationalReceiptCompletedByUserId', 'uniqueidentifier',1 UNION ALL
    SELECT 'RequestPoGroups',        'CompletedAtUtc',                      'datetime2',       1 UNION ALL
    SELECT 'RequestPoGroups',        'RowVersion',                          'timestamp',       0
)
SELECT
    [Check]  = '2. New columns present and correctly shaped',
    Verdict  = CASE WHEN COUNT(*) = 20 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Matching columns: ' + CAST(COUNT(*) AS varchar(10)) + ' of 20 expected'
FROM expected e
INNER JOIN INFORMATION_SCHEMA.COLUMNS c
        ON c.TABLE_NAME = e.TableName
       AND c.COLUMN_NAME = e.ColumnName
       AND c.DATA_TYPE = e.DataType
       AND CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END = e.IsNullable;

-- Detail listing, for diagnosing a FAIL above.
SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (c.TABLE_NAME = 'RequestPoGroups' AND c.COLUMN_NAME IN (
          'BillingDocumentType','FinalInvoiceStatus','FinalInvoiceAttachmentId','FinalInvoiceUploadedAtUtc',
          'FinalInvoiceUploadedByUserId','FinalInvoiceValidatedAtUtc','FinalInvoiceValidatedByUserId',
          'FinalInvoiceRejectionReason','FiscalReceiptAttachmentId','FiscalReceiptUploadedAtUtc',
          'FiscalReceiptUploadedByUserId','OperationalReceiptCompletedAtUtc',
          'OperationalReceiptCompletedByUserId','CompletedAtUtc','RowVersion'))
   OR (c.TABLE_NAME = 'Requests' AND c.COLUMN_NAME IN ('BillingDocumentType','CompletionCycleId','RowVersion'))
   OR (c.TABLE_NAME = 'Quotations' AND c.COLUMN_NAME = 'DocumentType')
   OR (c.TABLE_NAME = 'RequestStatusHistories' AND c.COLUMN_NAME = 'IdempotencyKey')
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;


/* --- CHECK 3: UNCLASSIFIED is the default, for new AND existing rows ----- */

SELECT
    [Check]  = '3. Every PO group defaults to UNCLASSIFIED',
    Verdict  = CASE WHEN SUM(CASE WHEN g.FinalInvoiceStatus <> 'UNCLASSIFIED' THEN 1 ELSE 0 END) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Groups: ' + CAST(COUNT(*) AS varchar(10))
             + ' | non-UNCLASSIFIED: '
             + CAST(SUM(CASE WHEN g.FinalInvoiceStatus <> 'UNCLASSIFIED' THEN 1 ELSE 0 END) AS varchar(10))
FROM dbo.RequestPoGroups g;
-- Release 1 activates nothing, so any other value would mean something wrote a dimension.

SELECT
    [Check]  = '3b. Column default constraint is UNCLASSIFIED',
    Verdict  = CASE WHEN COUNT(*) = 1 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Default constraints found on RequestPoGroups.FinalInvoiceStatus: ' + CAST(COUNT(*) AS varchar(10))
FROM sys.default_constraints dc
INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE OBJECT_NAME(dc.parent_object_id) = 'RequestPoGroups'
  AND c.name = 'FinalInvoiceStatus'
  AND dc.definition LIKE '%UNCLASSIFIED%';


/* --- CHECK 4: RowVersion is generated for every existing row ------------- */

SELECT
    [Check]  = '4. RowVersion populated on Requests',
    Verdict  = CASE WHEN SUM(CASE WHEN r.RowVersion IS NULL THEN 1 ELSE 0 END) = 0 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Rows: ' + CAST(COUNT(*) AS varchar(10))
             + ' | distinct RowVersions: ' + CAST(COUNT(DISTINCT CAST(r.RowVersion AS bigint)) AS varchar(10))
FROM dbo.Requests r;

SELECT
    [Check]  = '4b. RowVersion populated on RequestPoGroups',
    Verdict  = CASE WHEN SUM(CASE WHEN g.RowVersion IS NULL THEN 1 ELSE 0 END) = 0 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Rows: ' + CAST(COUNT(*) AS varchar(10))
             + ' | distinct RowVersions: ' + CAST(COUNT(DISTINCT CAST(g.RowVersion AS bigint)) AS varchar(10))
FROM dbo.RequestPoGroups g;


/* --- CHECK 5: history idempotency key + filtered unique index ------------ */

SELECT
    [Check]  = '5. IdempotencyKey is NULL on every historical row',
    Verdict  = CASE WHEN SUM(CASE WHEN h.IdempotencyKey IS NOT NULL THEN 1 ELSE 0 END) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'History rows: ' + CAST(COUNT(*) AS varchar(12))
             + ' | non-null keys: '
             + CAST(SUM(CASE WHEN h.IdempotencyKey IS NOT NULL THEN 1 ELSE 0 END) AS varchar(12))
FROM dbo.RequestStatusHistories h;
-- Release 1 introduces the column, not a writer. Non-zero here means a handler was activated.

SELECT
    [Check]  = '5b. Filtered UNIQUE index exists on IdempotencyKey',
    Verdict  = CASE WHEN COUNT(*) = 1 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = ISNULL(MAX(i.name + ' | unique=' + CAST(i.is_unique AS varchar(1))
                        + ' | filter=' + ISNULL(i.filter_definition, '(none)')),
                      'index not found')
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.RequestStatusHistories')
  AND i.name = 'UX_RequestStatusHistory_IdempotencyKey'
  AND i.is_unique = 1
  AND i.has_filter = 1;


/* --- CHECK 6: WAITING_FISCAL_RECEIPT seeded but unused ------------------- */

SELECT
    [Check]  = '6. WAITING_FISCAL_RECEIPT lookup row seeded',
    Verdict  = CASE WHEN COUNT(*) = 1 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Rows with Code = WAITING_FISCAL_RECEIPT: ' + CAST(COUNT(*) AS varchar(10))
FROM dbo.RequestStatuses
WHERE Code = 'WAITING_FISCAL_RECEIPT';

SELECT
    [Check]  = '6b. No request uses WAITING_FISCAL_RECEIPT yet',
    Verdict  = CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Requests in WAITING_FISCAL_RECEIPT: ' + CAST(COUNT(*) AS varchar(10))
FROM dbo.Requests r
INNER JOIN dbo.RequestStatuses s ON s.Id = r.StatusId
WHERE s.Code = 'WAITING_FISCAL_RECEIPT';

SELECT
    [Check]  = '6c. No PO group uses WAITING_FISCAL_RECEIPT yet',
    Verdict  = CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'PO groups in WAITING_FISCAL_RECEIPT: ' + CAST(COUNT(*) AS varchar(10))
FROM dbo.RequestPoGroups
WHERE Status = 'WAITING_FISCAL_RECEIPT';


/* --- CHECK 7: no RECEIPT attachment was renamed or reclassified ---------- */

SELECT
    [Check]  = '7. Legacy RECEIPT attachments intact, no new-type rows exist',
    Verdict  = CASE WHEN SUM(CASE WHEN AttachmentTypeCode IN ('FINAL_INVOICE','FISCAL_RECEIPT')
                                  THEN 1 ELSE 0 END) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'RECEIPT: ' + CAST(SUM(CASE WHEN AttachmentTypeCode = 'RECEIPT' THEN 1 ELSE 0 END) AS varchar(12))
             + ' | FINAL_INVOICE: ' + CAST(SUM(CASE WHEN AttachmentTypeCode = 'FINAL_INVOICE' THEN 1 ELSE 0 END) AS varchar(12))
             + ' | FISCAL_RECEIPT: ' + CAST(SUM(CASE WHEN AttachmentTypeCode = 'FISCAL_RECEIPT' THEN 1 ELSE 0 END) AS varchar(12))
FROM dbo.RequestAttachments;
-- Record the RECEIPT count BEFORE the migration and compare: it must be identical.


/* --- CHECK 8: no dimension and no completion identity was written -------- */

SELECT
    [Check]  = '8. No post-payment dimension was written',
    Verdict  = CASE WHEN SUM(CASE WHEN g.OperationalReceiptCompletedAtUtc IS NOT NULL
                                    OR g.FinalInvoiceAttachmentId IS NOT NULL
                                    OR g.FiscalReceiptAttachmentId IS NOT NULL
                                    OR g.CompletedAtUtc IS NOT NULL
                                    OR g.BillingDocumentType IS NOT NULL
                                   THEN 1 ELSE 0 END) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'PO groups carrying any dimension value: '
             + CAST(SUM(CASE WHEN g.OperationalReceiptCompletedAtUtc IS NOT NULL
                               OR g.FinalInvoiceAttachmentId IS NOT NULL
                               OR g.FiscalReceiptAttachmentId IS NOT NULL
                               OR g.CompletedAtUtc IS NOT NULL
                               OR g.BillingDocumentType IS NOT NULL
                              THEN 1 ELSE 0 END) AS varchar(10))
FROM dbo.RequestPoGroups g;

SELECT
    [Check]  = '8b. No Request has a completion identity yet',
    Verdict  = CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Requests with CompletionCycleId set: ' + CAST(COUNT(*) AS varchar(10))
FROM dbo.Requests
WHERE CompletionCycleId IS NOT NULL;

SELECT
    [Check]  = '8c. No request or quotation was classified',
    Verdict  = CASE WHEN (SELECT COUNT(*) FROM dbo.Requests WHERE BillingDocumentType IS NOT NULL)
                       + (SELECT COUNT(*) FROM dbo.Quotations WHERE DocumentType IS NOT NULL) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = 'Requests classified: ' + CAST((SELECT COUNT(*) FROM dbo.Requests WHERE BillingDocumentType IS NOT NULL) AS varchar(10))
             + ' | Quotations classified: ' + CAST((SELECT COUNT(*) FROM dbo.Quotations WHERE DocumentType IS NOT NULL) AS varchar(10));


/* --- CHECK 9: the new reconciliation table exists and is empty ----------- */

SELECT
    [Check]  = '9. FinalInvoiceReconciliations exists and is empty',
    Verdict  = CASE WHEN OBJECT_ID('dbo.FinalInvoiceReconciliations') IS NOT NULL
                     AND (SELECT COUNT(*) FROM dbo.FinalInvoiceReconciliations) = 0
                    THEN 'PASS' ELSE 'FAIL' END,
    Detail   = CASE WHEN OBJECT_ID('dbo.FinalInvoiceReconciliations') IS NULL
                    THEN 'table missing'
                    ELSE 'rows: ' + CAST((SELECT COUNT(*) FROM dbo.FinalInvoiceReconciliations) AS varchar(10))
               END;

PRINT '';
PRINT '=== End of verification. Every Verdict must read PASS. ===';
