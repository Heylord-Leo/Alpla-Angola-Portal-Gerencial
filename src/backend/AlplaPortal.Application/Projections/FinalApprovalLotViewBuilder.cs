using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Application.Projections;

/// <summary>
/// Builds the normalized, lot-aware <see cref="FinalApprovalLotViewDto"/> for one ApprovalBatch.
///
/// Pure function over already-projected DTOs (no DB, no EF) so it is fully unit-testable and can
/// run right after the request projection. It is the single place that resolves a batch item's
/// authoritative money — the selected quotation item's persisted LineTotal — instead of the
/// RequestLineItem estimate, which is 0 for quotation requests created without prices.
/// </summary>
public static class FinalApprovalLotViewBuilder
{
    /// <summary>Tolerance (currency minor unit) below which a snapshot/sum difference is treated as
    /// rounding, not a real inconsistency.</summary>
    private const decimal MonetaryTolerance = 0.01m;

    public static FinalApprovalLotViewDto Build(
        RequestApprovalBatchDto batch,
        IReadOnlyCollection<SavedQuotationDto> quotations,
        string? currencyCode = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        quotations ??= Array.Empty<SavedQuotationDto>();

        // Flatten every quotation item once, keyed by id, and remember its supplier via the
        // owning quotation snapshot. IGNORED lines are included here too — we only ever look up
        // by the batch's SelectedQuotationItemIds, which are never IGNORED.
        var itemById = new Dictionary<Guid, (SavedQuotationItemDto Item, string? Supplier)>();
        foreach (var q in quotations)
        {
            if (q.Items == null) continue;
            foreach (var qi in q.Items)
                itemById[qi.Id] = (qi, q.SupplierNameSnapshot);
        }

        var included = new List<FinalApprovalLotItemDto>();
        var supplierNames = new List<string>();
        decimal resolvedSum = 0m;
        bool hasUnresolved = false;

        foreach (var bi in batch.Items)
        {
            var lotItem = new FinalApprovalLotItemDto
            {
                RequestLineItemId = bi.RequestLineItemId,
                SelectedQuotationItemId = bi.SelectedQuotationItemId
            };

            // Candidate model: the winning candidate's FROZEN snapshot is the authoritative money
            // — a live quotation edit after submission must never change what was approved.
            var winnerCandidate = bi.SelectedCandidateId.HasValue
                ? bi.Candidates.FirstOrDefault(c => c.Id == bi.SelectedCandidateId.Value)
                : null;

            if (winnerCandidate != null)
            {
                lotItem.Description = winnerCandidate.Description;
                lotItem.Quantity = winnerCandidate.Quantity;
                lotItem.UnitCode = winnerCandidate.UnitText;
                lotItem.LineTotal = winnerCandidate.LineTotal;
                lotItem.SupplierName = winnerCandidate.SupplierName;
                lotItem.IsExtraItem = string.Equals(winnerCandidate.ReconciliationStatus, "EXTRA_ITEM", StringComparison.OrdinalIgnoreCase);

                resolvedSum += winnerCandidate.LineTotal;
                if (!string.IsNullOrWhiteSpace(winnerCandidate.SupplierName)
                    && !supplierNames.Contains(winnerCandidate.SupplierName))
                    supplierNames.Add(winnerCandidate.SupplierName);
            }
            else if (bi.SelectedQuotationItemId.HasValue
                && itemById.TryGetValue(bi.SelectedQuotationItemId.Value, out var found))
            {
                // Legacy buyer-selected winner (no candidate rows) — live lookup as before.
                lotItem.Description = found.Item.Description;
                lotItem.Quantity = found.Item.Quantity;
                lotItem.UnitCode = found.Item.UnitCode;
                lotItem.LineTotal = found.Item.LineTotal;
                lotItem.SupplierName = found.Supplier;
                lotItem.IsExtraItem = string.Equals(found.Item.ReconciliationStatus, "EXTRA_ITEM", StringComparison.OrdinalIgnoreCase);

                resolvedSum += found.Item.LineTotal;
                if (!string.IsNullOrWhiteSpace(found.Supplier)
                    && !supplierNames.Contains(found.Supplier!))
                    supplierNames.Add(found.Supplier!);
            }
            else
            {
                // No winner decided or winner could not be resolved — never fabricate a 0 total.
                lotItem.LineTotal = null;
                hasUnresolved = true;
            }

            included.Add(lotItem);
        }

        // Lot total: prefer the immutable snapshot; fall back to the resolved sum. When both exist
        // and disagree beyond rounding, surface an inconsistency rather than a false valid state.
        decimal lotTotal;
        bool inconsistent = false;
        if (batch.ApprovedTotalAmount.HasValue && batch.ApprovedTotalAmount.Value > 0)
        {
            lotTotal = batch.ApprovedTotalAmount.Value;
            if (!hasUnresolved && Math.Abs(batch.ApprovedTotalAmount.Value - resolvedSum) > MonetaryTolerance)
                inconsistent = true;
        }
        else
        {
            lotTotal = resolvedSum;
        }

        var ignored = batch.IgnoredLines ?? new List<BatchInformationalItemDto>();

        string? supplierLabel;
        string supplierHeading;
        if (supplierNames.Count == 0)
        {
            supplierLabel = null;
            supplierHeading = "Fornecedor do lote";
        }
        else if (supplierNames.Count == 1)
        {
            supplierLabel = supplierNames[0];
            supplierHeading = "Fornecedor do lote";
        }
        else
        {
            supplierLabel = $"{supplierNames.Count} fornecedores";
            supplierHeading = "Fornecedores do lote";
        }

        return new FinalApprovalLotViewDto
        {
            BatchNumber = batch.BatchNumber,
            LotTotal = lotTotal,
            CurrencyCode = currencyCode,
            IncludedItems = included,
            IgnoredLines = ignored,
            IncludedItemCount = included.Count,
            IgnoredItemCount = ignored.Count,
            SupplierNames = supplierNames,
            SupplierLabel = supplierLabel,
            SupplierHeading = supplierHeading,
            HasMonetaryInconsistency = inconsistent,
            HasUnresolvedItemValue = hasUnresolved
        };
    }
}
