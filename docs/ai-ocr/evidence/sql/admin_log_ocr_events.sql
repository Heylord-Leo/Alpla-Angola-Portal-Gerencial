-- AI OCR Compliance Evidence — Admin Log OCR Events
-- Execute in TEST environment only. Mask sensitive data before including in evidence.

SELECT TOP 15
    Id,
    TimestampUtc,
    Level,
    Source,
    EventType,
    Message,
    CorrelationId,
    UserEmail,
    -- Do NOT include ExceptionDetail or Payload with raw data
    CASE WHEN ExceptionDetail IS NOT NULL THEN '[HAS_EXCEPTION]' ELSE NULL END AS HasException,
    CASE WHEN Payload IS NOT NULL THEN '[HAS_PAYLOAD]' ELSE NULL END AS HasPayload
FROM AdminLogEntries
WHERE EventType LIKE '%OCR%'
   OR EventType LIKE '%EXTRACTION%'
   OR Source LIKE '%Extraction%'
   OR Source LIKE '%DocumentExtraction%'
ORDER BY TimestampUtc DESC;
