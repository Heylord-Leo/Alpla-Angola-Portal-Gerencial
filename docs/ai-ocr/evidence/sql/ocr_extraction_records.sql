-- ============================================================
-- AI OCR Extraction Records — Evidence Query
-- ============================================================
-- Purpose: Extract contract OCR extraction audit records.
-- Usage:   Execute against TEST database only.
--          Mask sensitive data before including in evidence.
-- ============================================================

SELECT TOP 10
    Id,
    ContractId,
    -- Mask user ID
    LEFT(CAST(TriggeredByUserId AS NVARCHAR(50)), 8) + '...' AS TriggeredByUserId_Masked,
    TriggeredAtUtc,
    ProcessedAtUtc,
    Status,
    ProviderName,
    RoutingStrategy,
    ChunkCount,
    TotalTokensUsed,
    QualityScore,
    IsPartial,
    ConflictsDetected,
    NativeTextDetected,
    -- Show raw JSON length, not content
    LEN(RawJsonResult) AS RawJsonLengthBytes,
    ErrorMessage,
    PromptVersion
FROM ContractOcrExtractionRecords
ORDER BY TriggeredAtUtc DESC;
