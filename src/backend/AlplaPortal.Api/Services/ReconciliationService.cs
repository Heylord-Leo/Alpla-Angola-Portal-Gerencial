using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Api.Services;

/// <summary>
/// Deterministic first-pass reconciliation engine.
/// Compares requester items against OCR-extracted items to produce
/// match/miss/extra records. Does not use AI/ML — uses practical
/// business rules: catalog code matching, normalized description
/// comparison, fuzzy similarity, unit/quantity proximity hints.
/// </summary>
public static class ReconciliationService
{
    /// <summary>
    /// Generate reconciliation records for a given extraction batch.
    /// </summary>
    public static List<ReconciliationRecord> GenerateReconciliation(
        Guid requestId,
        Guid extractionBatchId,
        List<RequestLineItem> requesterItems,
        List<OcrExtractedItem> ocrItems)
    {
        var records = new List<ReconciliationRecord>();
        var matchedOcrIds = new HashSet<Guid>();
        var matchedRequesterIds = new HashSet<Guid>();

        // Phase 1: Catalog code matching (highest confidence)
        foreach (var reqItem in requesterItems.Where(r => r.ItemCatalogId.HasValue && r.ItemCatalogItem != null))
        {
            var catalogCode = reqItem.ItemCatalogItem!.Code.ToUpperInvariant();
            var bestMatch = ocrItems
                .Where(o => !matchedOcrIds.Contains(o.Id))
                .FirstOrDefault(o => NormalizeText(o.RawDescription).Contains(catalogCode));

            if (bestMatch != null)
            {
                records.Add(CreateMatchRecord(requestId, extractionBatchId, reqItem, bestMatch,
                    "EXACT_MATCH", 1.0m, "CATALOG_CODE"));
                matchedOcrIds.Add(bestMatch.Id);
                matchedRequesterIds.Add(reqItem.Id);
            }
        }

        // Phase 2: Exact normalized description matching
        foreach (var reqItem in requesterItems.Where(r => !matchedRequesterIds.Contains(r.Id)))
        {
            var normalizedReq = NormalizeText(reqItem.Description);
            var exactMatch = ocrItems
                .Where(o => !matchedOcrIds.Contains(o.Id))
                .FirstOrDefault(o => NormalizeText(o.RawDescription) == normalizedReq);

            if (exactMatch != null)
            {
                records.Add(CreateMatchRecord(requestId, extractionBatchId, reqItem, exactMatch,
                    "EXACT_MATCH", 0.95m, "DESCRIPTION_EXACT"));
                matchedOcrIds.Add(exactMatch.Id);
                matchedRequesterIds.Add(reqItem.Id);
            }
        }

        // Phase 3: Fuzzy description matching (containment + similarity)
        foreach (var reqItem in requesterItems.Where(r => !matchedRequesterIds.Contains(r.Id)))
        {
            var normalizedReq = NormalizeText(reqItem.Description);
            OcrExtractedItem? bestFuzzy = null;
            decimal bestScore = 0;

            foreach (var ocrItem in ocrItems.Where(o => !matchedOcrIds.Contains(o.Id)))
            {
                var normalizedOcr = NormalizeText(ocrItem.RawDescription);
                var score = CalculateSimilarity(normalizedReq, normalizedOcr);

                // Boost score if units match
                if (reqItem.UnitId.HasValue && ocrItem.ResolvedUnitId.HasValue &&
                    reqItem.UnitId.Value == ocrItem.ResolvedUnitId.Value)
                {
                    score = Math.Min(1.0m, score + 0.10m);
                }

                // Boost score if quantities are close
                if (reqItem.Quantity > 0 && ocrItem.Quantity.HasValue && ocrItem.Quantity.Value > 0)
                {
                    var qtyRatio = Math.Min(reqItem.Quantity, ocrItem.Quantity.Value) /
                                   Math.Max(reqItem.Quantity, ocrItem.Quantity.Value);
                    if (qtyRatio > 0.8m) score = Math.Min(1.0m, score + 0.05m);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFuzzy = ocrItem;
                }
            }

            if (bestFuzzy != null && bestScore >= 0.40m)
            {
                var status = bestScore >= 0.80m ? "PROBABLE_MATCH" : "REVIEW_REQUIRED";
                records.Add(CreateMatchRecord(requestId, extractionBatchId, reqItem, bestFuzzy,
                    status, bestScore, "DESCRIPTION_FUZZY"));
                matchedOcrIds.Add(bestFuzzy.Id);
                matchedRequesterIds.Add(reqItem.Id);
            }
        }

        // Phase 4: Remaining unmatched requester items → MISSING_REQUESTED_ITEM
        foreach (var reqItem in requesterItems.Where(r => !matchedRequesterIds.Contains(r.Id)))
        {
            records.Add(new ReconciliationRecord
            {
                RequestId = requestId,
                ExtractionBatchId = extractionBatchId,
                RequesterItemId = reqItem.Id,
                OcrExtractedItemId = null,
                MatchStatus = "MISSING_REQUESTED_ITEM",
                MatchConfidence = 0,
                MatchStrategy = "NONE",
                BuyerReviewStatus = "PENDING"
            });
        }

        // Phase 5: Remaining unmatched OCR items → EXTRA_SUPPLIER_ITEM
        foreach (var ocrItem in ocrItems.Where(o => !matchedOcrIds.Contains(o.Id)))
        {
            records.Add(new ReconciliationRecord
            {
                RequestId = requestId,
                ExtractionBatchId = extractionBatchId,
                RequesterItemId = null,
                OcrExtractedItemId = ocrItem.Id,
                MatchStatus = "EXTRA_SUPPLIER_ITEM",
                MatchConfidence = 0,
                MatchStrategy = "NONE",
                BuyerReviewStatus = "PENDING"
            });
        }

        return records;
    }

    private static ReconciliationRecord CreateMatchRecord(
        Guid requestId, Guid extractionBatchId,
        RequestLineItem reqItem, OcrExtractedItem ocrItem,
        string status, decimal confidence, string strategy)
    {
        decimal? qtyDivergence = null;
        if (ocrItem.Quantity.HasValue)
            qtyDivergence = ocrItem.Quantity.Value - reqItem.Quantity;

        bool unitDivergence = reqItem.UnitId.HasValue && ocrItem.ResolvedUnitId.HasValue &&
                              reqItem.UnitId.Value != ocrItem.ResolvedUnitId.Value;

        return new ReconciliationRecord
        {
            RequestId = requestId,
            ExtractionBatchId = extractionBatchId,
            RequesterItemId = reqItem.Id,
            OcrExtractedItemId = ocrItem.Id,
            MatchStatus = status,
            MatchConfidence = confidence,
            MatchStrategy = strategy,
            QuantityDivergence = qtyDivergence,
            UnitDivergence = unitDivergence,
            BuyerReviewStatus = "PENDING"
        };
    }

    /// <summary>
    /// Normalize text for comparison: lowercase, trim, collapse whitespace,
    /// remove accents, strip common noise characters.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Trim().ToUpperInvariant();

        // Remove accents (basic Portuguese support)
        normalized = normalized
            .Replace("Á", "A").Replace("À", "A").Replace("Ã", "A").Replace("Â", "A")
            .Replace("É", "E").Replace("Ê", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O").Replace("Ô", "O").Replace("Õ", "O")
            .Replace("Ú", "U").Replace("Ü", "U")
            .Replace("Ç", "C");

        // Collapse whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

        // Remove noise punctuation
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[.,;:\-\/\(\)\[\]\""]", "");

        return normalized.Trim();
    }

    /// <summary>
    /// Calculate text similarity using containment and character overlap.
    /// Returns 0.0–1.0.
    /// </summary>
    private static decimal CalculateSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        // Check containment (one contains the other)
        if (a.Contains(b) || b.Contains(a))
        {
            var ratio = (decimal)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            return Math.Max(0.70m, ratio);
        }

        // Word overlap approach
        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (wordsA.Count == 0 || wordsB.Count == 0) return 0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        return union > 0 ? (decimal)intersection / union : 0;
    }
}
