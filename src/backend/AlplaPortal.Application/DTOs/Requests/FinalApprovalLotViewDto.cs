namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Normalized, lot-aware view model for the Final Approval screen of a single ApprovalBatch.
/// Computed once on the backend (authoritative) from the batch's items, the batch's IGNORED
/// lines and the request's quotations. The Final Approval UI renders straight from this — it
/// never re-derives line totals from quantity × unit price nor sums money on its own.
///
/// Only the current lot contributes to <see cref="LotTotal"/>, <see cref="IncludedItemCount"/>
/// and <see cref="SupplierNames"/>. IGNORED lines are carried for audit in <see cref="IgnoredLines"/>
/// but never counted in the lot.
/// </summary>
public class FinalApprovalLotViewDto
{
    public int BatchNumber { get; set; }

    /// <summary>Authoritative approved total for THIS lot: the immutable ApprovedTotalAmount snapshot
    /// when present, otherwise the sum of the included items' selected-quotation line totals.</summary>
    public decimal LotTotal { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Items belonging to this lot (requested + included EXTRA_ITEM), each carrying the
    /// authoritative selected-quotation line total — never the RequestLineItem estimate.</summary>
    public List<FinalApprovalLotItemDto> IncludedItems { get; set; } = new();

    /// <summary>IGNORED quotation lines from the batch's contributing quotation(s). Read-only, for
    /// audit; excluded from <see cref="LotTotal"/> and <see cref="IncludedItemCount"/>.</summary>
    public List<BatchInformationalItemDto> IgnoredLines { get; set; } = new();

    public int IncludedItemCount { get; set; }
    public int IgnoredItemCount { get; set; }

    /// <summary>Distinct supplier names contributing winners to this lot.</summary>
    public List<string> SupplierNames { get; set; } = new();

    /// <summary>Resolved supplier value for the header: the single supplier name, or "N fornecedores".
    /// Null only when the lot genuinely has no resolvable supplier.</summary>
    public string? SupplierLabel { get; set; }

    /// <summary>Header caption paired with <see cref="SupplierLabel"/>: "Fornecedor do lote" for one
    /// supplier, "Fornecedores do lote" for several.</summary>
    public string SupplierHeading { get; set; } = "Fornecedor do lote";

    /// <summary>True when the ApprovedTotalAmount snapshot disagrees with the sum of included line
    /// totals — the UI must warn instead of showing a falsely consistent value.</summary>
    public bool HasMonetaryInconsistency { get; set; }

    /// <summary>True when at least one included item has no resolvable selected-quotation line total
    /// (missing winner value). The UI must warn on that item instead of silently showing 0.</summary>
    public bool HasUnresolvedItemValue { get; set; }
}

/// <summary>One item belonging to the Final Approval lot, valued from its selected quotation item.</summary>
public class FinalApprovalLotItemDto
{
    public Guid RequestLineItemId { get; set; }

    /// <summary>Winning quotation item. Null before the Area winner decision (candidate model) —
    /// a state the Final screen never legitimately sees, surfaced via HasUnresolvedItemValue.</summary>
    public Guid? SelectedQuotationItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? UnitCode { get; set; }

    /// <summary>Authoritative selected-quotation line total (IVA included). Null = the winning
    /// quotation item could not be resolved; callers must treat null as "unknown", never 0.</summary>
    public decimal? LineTotal { get; set; }

    public string? SupplierName { get; set; }

    /// <summary>True when the winning quotation line is a buyer-added EXTRA_ITEM.</summary>
    public bool IsExtraItem { get; set; }
}
