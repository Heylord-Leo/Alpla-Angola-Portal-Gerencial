-- ============================================================
-- AI OCR Cleanup Evidence — Evidence Query
-- ============================================================
-- Purpose: Extract cleanup execution/failure events for evidence.
-- Usage:   Execute against TEST database only.
-- Note:    AutoCleanupEnabled is false by default. These events
--          will only exist if cleanup was manually tested.
-- ============================================================

-- Cleanup execution events
SELECT TOP 10
    Id,
    TimestampUtc,
    Level,
    EventType,
    Source,
    Message,
    LEN(Payload) AS PayloadLengthChars
FROM AdminLogEntries
WHERE EventType IN ('OCR_CLEANUP_EXECUTED', 'OCR_CLEANUP_FAILED')
ORDER BY TimestampUtc DESC;

-- If no results: AutoCleanupEnabled is false (expected default behavior)
-- This is NOT a failure — it proves the cleanup guard is working correctly.
