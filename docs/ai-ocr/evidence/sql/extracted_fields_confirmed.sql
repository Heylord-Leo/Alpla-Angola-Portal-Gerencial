-- AI OCR Compliance Evidence — Confirmed/Overridden/Discarded Fields
-- Execute in TEST environment only. Mask sensitive data before including in evidence.

-- Confirmed fields (user accepted AI suggestion)
SELECT TOP 5
    Id, FieldName, RawExtractedValue, NormalisedValue, ConfidenceScore,
    DisplayHint, ConfirmedByUser, ConfirmedAtUtc, ConfirmedByUserId,
    WasOverridden, FinalSavedValue, DiscardedByUser
FROM ContractOcrExtractedFields
WHERE ConfirmedByUser = 1 AND WasOverridden = 0
ORDER BY ConfirmedAtUtc DESC;

-- Overridden fields (user changed AI value before confirming)
SELECT TOP 5
    Id, FieldName, RawExtractedValue, NormalisedValue, ConfidenceScore,
    DisplayHint, ConfirmedByUser, ConfirmedAtUtc, ConfirmedByUserId,
    WasOverridden, FinalSavedValue, DiscardedByUser
FROM ContractOcrExtractedFields
WHERE WasOverridden = 1
ORDER BY ConfirmedAtUtc DESC;

-- Discarded fields (user rejected AI suggestion)
SELECT TOP 5
    Id, FieldName, RawExtractedValue, NormalisedValue, ConfidenceScore,
    DisplayHint, ConfirmedByUser, DiscardedByUser
FROM ContractOcrExtractedFields
WHERE DiscardedByUser = 1
ORDER BY Id DESC;
