-- AI OCR Compliance Evidence — Extraction Records Sample Query
-- Execute in TEST environment only. Mask sensitive data before including in evidence.
-- Store results as SCR-22 screenshot or export to CSV.

SELECT TOP 5
    Id,
    ContractId,
    TriggeredByUserId,
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
    LEN(RawJsonResult) AS RawJsonLengthBytes,
    ErrorMessage
FROM ContractOcrExtractionRecords
ORDER BY TriggeredAtUtc DESC;
