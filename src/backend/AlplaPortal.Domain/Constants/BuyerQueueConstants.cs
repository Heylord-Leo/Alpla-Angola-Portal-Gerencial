namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Canonical Buyer-queue vocabulary (Phase 1). Single source of truth for the server-derived Buyer
/// operational model consumed by GET /api/v1/buyer/queue. The frontend must NOT re-derive these — it
/// consumes the projected codes/labels. See docs/BUYER_QUEUE_CANONICAL_MODEL.md.
/// </summary>
public static class BuyerQueueConstants
{
    /// <summary>Approved deadline threshold (Phase 0 §6). Centralized here — never hardcode "3".</summary>
    public const int ApproachingDeadlineDays = 3;

    /// <summary>Request-level Buyer operational state (state labels are professional descriptions;
    /// verbs live only on the next-action, never "Precisa Cotar").</summary>
    public static class OperationalStates
    {
        public const string NeedsQuotation = "NEEDS_QUOTATION";
        public const string PartialCoverage = "PARTIAL_COVERAGE";
        public const string ReadyForApproval = "READY_FOR_APPROVAL";
        public const string AwaitingApproval = "AWAITING_APPROVAL";
        public const string AdjustmentRequired = "ADJUSTMENT_REQUIRED";
        public const string AwaitingRequesterDecision = "AWAITING_REQUESTER_DECISION";
        public const string CompletedForBuyer = "COMPLETED_FOR_BUYER";
        public const string NoBuyerAction = "NO_BUYER_ACTION";

        public static string Label(string code) => code switch
        {
            NeedsQuotation => "Cotação Pendente",
            PartialCoverage => "Cobertura Parcial",
            ReadyForApproval => "Pronto para Aprovação",
            AwaitingApproval => "Em Aprovação",
            AdjustmentRequired => "Ajuste Solicitado",
            AwaitingRequesterDecision => "Aguardando Decisão",
            CompletedForBuyer => "Concluído para Compras",
            NoBuyerAction => "Sem Ação do Comprador",
            _ => code
        };

        /// <summary>States hidden from the default active queue (still queryable via includeCompleted).</summary>
        public static readonly string[] HiddenByDefault = { CompletedForBuyer, NoBuyerAction };
    }

    /// <summary>Server-derived next Buyer action codes.</summary>
    public static class ActionCodes
    {
        public const string AddQuotation = "ADD_QUOTATION";
        public const string SubmitBatch = "SUBMIT_BATCH";
        public const string ResolveAdjustment = "RESOLVE_ADJUSTMENT";
        public const string None = "NONE";
    }

    /// <summary>Per-item mutually-exclusive coverage buckets (Phase 0 §4).</summary>
    public static class CoverageBuckets
    {
        public const string CancelledDeleted = "CANCELLED_DELETED";
        public const string Approved = "APPROVED";
        public const string InActiveBatch = "IN_ACTIVE_BATCH";
        public const string ClosedNotQuoted = "CLOSED_NOT_QUOTED";
        public const string NotQuotedProposed = "NOT_QUOTED_PROPOSED";
        public const string NotQuotedAccepted = "NOT_QUOTED_ACCEPTED";
        public const string QuotedReadyForBatch = "QUOTED_READY_FOR_BATCH";
        public const string PendingQuotation = "PENDING_QUOTATION";
    }

    /// <summary>Request-level coverage status.</summary>
    public static class CoverageStatuses
    {
        public const string NotCovered = "NOT_COVERED";
        public const string PartiallyCovered = "PARTIALLY_COVERED";
        public const string FullyCovered = "FULLY_COVERED";
        public const string AwaitingDecision = "AWAITING_DECISION";
    }

    public static class DeadlineConditions
    {
        public const string Overdue = "OVERDUE";
        public const string DueToday = "DUE_TODAY";
        public const string Approaching = "APPROACHING";
        public const string WithinDeadline = "WITHIN_DEADLINE";
        public const string None = "NONE"; // no NeedByDate
    }

    public static class PriorityBands
    {
        public const string ExceptionOrOverdue = "EXCEPTION_OR_OVERDUE";
        public const string Standard = "STANDARD";
    }

    public static class AttentionSeverities
    {
        public const string Blocking = "BLOCKING";
        public const string UrgentDeadline = "URGENT_DEADLINE";
        public const string Warning = "WARNING";
    }

    public static class AttentionCodes
    {
        public const string AdjustmentRequired = "ADJUSTMENT_REQUIRED";
        public const string Overdue = "OVERDUE";
        public const string DueToday = "DUE_TODAY";
        public const string SupersededBatch = "SUPERSEDED_BATCH";
        public const string UnassignedNearDeadline = "UNASSIGNED_NEAR_DEADLINE";
    }

    public static class OwnershipStates
    {
        public const string Mine = "MINE";
        public const string Unassigned = "UNASSIGNED";
        public const string Other = "OTHER";
    }
}
