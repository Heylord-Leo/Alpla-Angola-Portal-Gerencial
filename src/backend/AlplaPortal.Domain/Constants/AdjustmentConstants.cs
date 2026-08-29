using System.Collections.Generic;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Adjustment V2 (Phase 2 — dormant domain model): closed catalogs for the structured adjustment
/// cycle persistence. Values follow the repository's string-status convention (see
/// RequestConstants.ApprovalBatchStatuses). Phase 2 persists these catalogs only — no workflow
/// reads or writes them yet; activation begins in Phase 3 (approver structured request).
/// </summary>
public static class AdjustmentConstants
{
    /// <summary>Which approval stage requested the adjustment cycle.</summary>
    public static class SourceStages
    {
        public const string Area = "AREA";
        public const string Final = "FINAL";
    }

    /// <summary>
    /// Adjustment-cycle lifecycle. A SEPARATE state machine from ApprovalBatch.Status — batch
    /// statuses (AREA_ADJUSTMENT / FINAL_ADJUSTMENT / WAITING_*_APPROVAL) and the request scalar
    /// remain untouched by design. The transient REQUESTED state of the approved spec is never
    /// persisted: a cycle is stored directly in its first routed state.
    /// </summary>
    public static class States
    {
        public const string WaitingRequester = "WAITING_REQUESTER";
        public const string WaitingBuyer = "WAITING_BUYER";
        public const string Resubmitted = "RESUBMITTED";
        public const string Cancelled = "CANCELLED";

        /// <summary>States in which the cycle is open (drives the one-open-cycle-per-batch
        /// filtered unique index via the computed IsOpen column).</summary>
        public static readonly string[] Open = { WaitingRequester, WaitingBuyer };
    }

    /// <summary>Actor of one mandatory "Resposta ao reajuste" hand-off resolution.</summary>
    public static class ActorTypes
    {
        public const string Requester = "REQUESTER";
        public const string Buyer = "BUYER";
    }

    /// <summary>
    /// Approved reason catalog (Product decisions closed 2026-08-28; SUPPLIER is deliberately
    /// DISTINCT from SUPPLIER_DELIVERY_TIME per decision OD5).
    /// </summary>
    public static class ReasonCodes
    {
        // Buyer-owned
        public const string PriceNegotiation = "PRICE_NEGOTIATION";
        public const string NewQuotation = "NEW_QUOTATION";
        public const string Supplier = "SUPPLIER";
        public const string SupplierDeliveryTime = "SUPPLIER_DELIVERY_TIME";
        public const string PaymentTerms = "PAYMENT_TERMS";
        public const string Documentation = "DOCUMENTATION";
        public const string BatchComposition = "BATCH_COMPOSITION";
        public const string ExtraQuotationItem = "EXTRA_QUOTATION_ITEM";
        public const string Other = "OTHER";

        // Requester-first (Buyer review second)
        public const string RequestedQuantity = "REQUESTED_QUANTITY";
        public const string Specification = "SPECIFICATION";
        public const string RequestedUnit = "REQUESTED_UNIT";
        public const string NeededByDate = "NEEDED_BY_DATE"; // advisory/non-blocking in future logic (OD1)
        public const string MissingItem = "MISSING_ITEM";
        public const string RemoveRequestItem = "REMOVE_REQUEST_ITEM";

        public static readonly string[] All =
        {
            PriceNegotiation, NewQuotation, Supplier, SupplierDeliveryTime, PaymentTerms,
            Documentation, BatchComposition, ExtraQuotationItem, Other,
            RequestedQuantity, Specification, RequestedUnit, NeededByDate, MissingItem, RemoveRequestItem
        };

        /// <summary>Reasons that route the cycle to the Requester first (owner map).</summary>
        public static readonly HashSet<string> RequesterOwned = new()
        {
            RequestedQuantity, Specification, RequestedUnit, NeededByDate, MissingItem, RemoveRequestItem
        };
    }

    /// <summary>
    /// Controlled business-field catalog for the Requester-edit audit (never a generic database
    /// audit framework — only these fields are representable).
    /// </summary>
    public static class FieldCodes
    {
        public const string RequestedQuantity = "REQUESTED_QUANTITY";
        public const string Specification = "SPECIFICATION";
        public const string RequestedUnit = "REQUESTED_UNIT";
        public const string NeededByDate = "NEEDED_BY_DATE";

        public static readonly string[] All = { RequestedQuantity, Specification, RequestedUnit, NeededByDate };
    }

    /// <summary>Buyer review state of one flagged candidate option (future actions CONFIRM /
    /// REFRESH / REPLACE / REMOVE resolve a PENDING row).</summary>
    public static class CandidateReviewStates
    {
        public const string Pending = "PENDING";
        public const string Confirmed = "CONFIRMED";
        public const string Refreshed = "REFRESHED";
        public const string Replaced = "REPLACED";
        public const string Removed = "REMOVED";

        public static readonly string[] All = { Pending, Confirmed, Refreshed, Replaced, Removed };
    }

    /// <summary>Which blocking Requester edit flagged the candidate (NEEDED_BY_DATE never
    /// creates a review row — advisory only, per decision OD1).</summary>
    public static class CandidateReviewTriggers
    {
        public const string QuantityChanged = "QUANTITY_CHANGED";
        public const string SpecificationChanged = "SPECIFICATION_CHANGED";
        public const string UnitChanged = "UNIT_CHANGED";
    }
}
