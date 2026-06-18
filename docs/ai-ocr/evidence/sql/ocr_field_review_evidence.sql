-- ============================================================
-- AI OCR Field Review Evidence — Evidence Query
-- ============================================================
-- Purpose: Extract human review actions on OCR-extracted fields
--          showing confirm/override/discard audit trail.
-- Usage:   Execute against TEST database only.
--          Mask extracted values if they contain sensitive data.
-- ============================================================

-- Confirmed fields (user accepted AI suggestion)
SELECT TOP 5
    Id,
    FieldName,
    -- Mask values: show first 3 chars + '...'
    LEFT(RawExtractedValue, 3) + '***' AS RawExtractedValue_Masked,
    LEFT(NormalisedValue, 3) + '***' AS NormalisedValue_Masked,
    ConfidenceScore,
    DisplayHint,
    ConfirmedByUser,
    ConfirmedAtUtc,
    WasOverridden,
    DiscardedByUser,
    'CONFIRMED' AS ReviewAction
FROM ContractOcrExtractedFields
WHERE ConfirmedByUser = 1
ORDER BY ConfirmedAtUtc DESC;

-- Overridden fields (user edited AI suggestion)
SELECT TOP 5
    Id,
    FieldName,
    LEFT(RawExtractedValue, 3) + '***' AS RawExtractedValue_Masked,
    LEFT(FinalSavedValue, 3) + '***' AS FinalSavedValue_Masked,
    ConfidenceScore,
    WasOverridden,
    ConfirmedAtUtc,
    'OVERRIDDEN' AS ReviewAction
FROM ContractOcrExtractedFields
WHERE WasOverridden = 1
ORDER BY ConfirmedAtUtc DESC;

-- Discarded fields (user rejected AI suggestion)
SELECT TOP 5
    Id,
    FieldName,
    LEFT(RawExtractedValue, 3) + '***' AS RawExtractedValue_Masked,
    ConfidenceScore,
    DiscardedByUser,
    'DISCARDED' AS ReviewAction
FROM ContractOcrExtractedFields
WHERE DiscardedByUser = 1
ORDER BY Id DESC;
