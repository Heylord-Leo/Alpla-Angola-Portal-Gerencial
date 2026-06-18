-- ============================================================
-- AI OCR System Log Events — Evidence Query
-- ============================================================
-- Purpose: Extract OCR-related events from the AdminLogEntries table
--          for compliance evidence.
-- Usage:   Execute against TEST database only.
--          Mask sensitive data before including in evidence reports.
-- ============================================================

SELECT TOP 20
    Id,
    TimestampUtc,
    Level,
    EventType,
    Source,
    Message,
    CorrelationId,
    -- Mask user email: show domain only
    CASE
        WHEN UserEmail IS NOT NULL
        THEN CONCAT('***@', SUBSTRING(UserEmail, CHARINDEX('@', UserEmail) + 1, LEN(UserEmail)))
        ELSE NULL
    END AS UserEmail_Masked,
    -- Show payload length, not content (may contain metadata)
    LEN(Payload) AS PayloadLengthChars,
    -- Show exception summary if present
    LEFT(ExceptionDetail, 100) AS ExceptionSummary
FROM AdminLogEntries
WHERE EventType IN (
    'OCR_EXTRACTION_STARTED',
    'OCR_EXTRACTION_COMPLETED',
    'OCR_EXTRACTION_FAILED',
    'OCR_EXTRACTION_TIMEOUT',
    'OCR_FEATURE_DISABLED',
    'OCR_MODULE_BLOCKED',
    'OCR_DOCUMENT_TYPE_BLOCKED',
    'OCR_CLEANUP_EXECUTED',
    'OCR_CLEANUP_FAILED'
)
ORDER BY TimestampUtc DESC;
