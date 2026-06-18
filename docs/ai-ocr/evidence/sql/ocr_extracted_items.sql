-- AI OCR Compliance Evidence — OcrExtractedItem Records (Invoice/Proforma)
-- Execute in TEST environment only. Mask sensitive data before including in evidence.

SELECT TOP 10
    Id,
    RequestId,
    ExtractionBatchId,
    AttachmentId,
    LineNumber,
    LEFT(RawDescription, 50) AS RawDescriptionTruncated,
    Quantity,
    RawUnit,
    ResolvedUnitId,
    UnitPrice,
    DiscountAmount,
    DiscountPercent,
    TaxRate,
    LineTotal,
    QualityScore,
    ProviderName,
    ExtractedAtUtc
FROM OcrExtractedItems
ORDER BY ExtractedAtUtc DESC;
