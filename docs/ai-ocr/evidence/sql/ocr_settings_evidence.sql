-- ============================================================
-- AI OCR Settings Evidence — Evidence Query
-- ============================================================
-- Purpose: Extract current OCR/extraction settings from the database
--          for configuration compliance evidence.
-- Usage:   Execute against TEST database only.
--          API keys and secrets MUST be masked.
-- ============================================================

SELECT
    Id,
    Category,
    [Key],
    CASE
        WHEN [Key] LIKE '%ApiKey%' OR [Key] LIKE '%Secret%' OR [Key] LIKE '%Token%' OR [Key] LIKE '%Password%'
        THEN '[REDACTED]'
        ELSE LEFT([Value], 50)
    END AS Value_Masked,
    LEN([Value]) AS ValueLength,
    IsEncrypted,
    UpdatedAtUtc,
    UpdatedBy
FROM IntegrationSettings
WHERE Category IN ('DocumentExtraction', 'OPENAI', 'AzureDocumentIntelligence')
ORDER BY Category, [Key];

-- Alternative: If settings are stored in a different table
-- Check DocumentExtractionSettings table
SELECT TOP 1
    Id,
    DefaultProvider,
    IsEnabled,
    GlobalTimeoutSeconds,
    -- Mask sensitive provider-specific settings
    CASE WHEN OpenAiApiKey IS NOT NULL THEN '[CONFIGURED]' ELSE '[NOT SET]' END AS OpenAiApiKey_Status,
    OpenAiModel,
    OpenAiEndpoint,
    DebugRawPayloadLogging,
    RequireHumanConfirmation,
    AutoCleanupEnabled,
    DebugFileRetentionDays,
    RawJsonResultRetentionDays,
    UpdatedAtUtc
FROM DocumentExtractionSettings
ORDER BY UpdatedAtUtc DESC;
