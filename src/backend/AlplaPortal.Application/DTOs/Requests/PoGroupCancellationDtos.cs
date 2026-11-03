using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

// ── Phase 1 P.O. group cancellation / return to quotation (docs/PO_GROUP_CANCELLATION.md) ──

/// <summary>Buyer command: cancel one P.O. group after final approval.</summary>
public class CancelPoGroupRequestDto
{
    /// <summary>RESTART_QUOTATION | CANCEL_ITEMS.</summary>
    public string? Kind { get; set; }
    public string? ReasonCode { get; set; }
    /// <summary>Mandatory explanation (20–1000 characters).</summary>
    public string? Reason { get; set; }
    /// <summary>Required when the group ever had a P.O.: PENDING_EXTERNAL | DECLARED_CANCELLED | UNKNOWN.</summary>
    public string? EcfStatus { get; set; }
    /// <summary>Required for DECLARED_CANCELLED (e.g. "ECF10 2026/364").</summary>
    public string? EcfReference { get; set; }
    public string? EcfDeclarationNote { get; set; }
    public Guid? EcfEvidenceAttachmentId { get; set; }
    /// <summary>Base64 of the group RowVersion the client displayed; a mismatch is 409 CANCELLATION_CONCURRENT_CHANGE.</summary>
    public string? ExpectedGroupRowVersion { get; set; }
}

/// <summary>Buyer command: update the ECF declaration while Finance confirmation is pending or rejected.</summary>
public class UpdateEcfDeclarationDto
{
    public string? EcfStatus { get; set; }
    public string? EcfReference { get; set; }
    public string? Note { get; set; }
    public Guid? EvidenceAttachmentId { get; set; }
    public string? ExpectedRowVersion { get; set; }
}

/// <summary>Finance command: confirm or reject the Buyer's declaration (note mandatory on rejection).</summary>
public class FinanceCancellationReviewDto
{
    public string? Note { get; set; }
    public string? ExpectedRowVersion { get; set; }
}

public class PoGroupCancellationItemDto
{
    public Guid RequestLineItemId { get; set; }
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? PreviousLineItemStatusCode { get; set; }
}

public class PoGroupCancellationEcfEventDto
{
    public string EventType { get; set; } = string.Empty;
    public string EcfStatusAfter { get; set; } = string.Empty;
    public string FinanceStatusAfter { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public Guid? EvidenceAttachmentId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string ActorRoleSnapshot { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}

/// <summary>Actions the CURRENT caller may perform on a cancellation (server-decided; the UI never recomputes).</summary>
public class PoGroupCancellationActionsDto
{
    public bool CanUpdateDeclaration { get; set; }
    public bool CanResendNotification { get; set; }
    public bool CanFinanceReview { get; set; }
    public string? FinanceReviewBlockReason { get; set; }
}

/// <summary>Full read model of one cancellation (Buyer declaration and Finance confirmation kept visibly apart).</summary>
public class PoGroupCancellationDto
{
    public Guid Id { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PreviousGroupStatus { get; set; } = string.Empty;
    public string? PurchaseOrderNumberSnapshot { get; set; }
    public decimal TotalAmountSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public int? ApprovalBatchNumberSnapshot { get; set; }
    public Guid? OriginFinanceReturnHistoryId { get; set; }
    public Guid CancelledByUserId { get; set; }
    public string? CancelledByName { get; set; }
    public DateTime CancelledAtUtc { get; set; }

    // Buyer declaration
    public bool FinanceConfirmationRequired { get; set; }
    public string EcfStatus { get; set; } = string.Empty;
    public string? EcfReference { get; set; }
    public string? EcfDeclarationNote { get; set; }
    public Guid? EcfEvidenceAttachmentId { get; set; }
    public Guid? EcfDeclaredByUserId { get; set; }
    public string? EcfDeclaredByName { get; set; }
    public DateTime? EcfDeclaredAtUtc { get; set; }

    // Finance confirmation
    public string FinanceConfirmationStatus { get; set; } = string.Empty;
    public Guid? FinanceConfirmedByUserId { get; set; }
    public string? FinanceConfirmedByName { get; set; }
    public DateTime? FinanceConfirmedAtUtc { get; set; }
    public string? FinanceConfirmationNote { get; set; }
    public string? FinanceReviewedByName { get; set; }
    public DateTime? FinanceReviewedAtUtc { get; set; }
    /// <summary>
    /// v2.249.4 — true when the last Finance review (confirmation OR rejection) was made on the CURRENT declaration
    /// (reviewed after the last declaration update). False while a re-declared cancellation awaits a new decision:
    /// the reviewer/note columns then describe the PREVIOUS review, surfaced as <see cref="PreviousFinanceReview"/>.
    /// </summary>
    public bool FinanceReviewAppliesToCurrentDeclaration { get; set; }
    /// <summary>The review that preceded the current declaration (null when none, or when the review is current).</summary>
    public PoGroupCancellationPreviousReviewDto? PreviousFinanceReview { get; set; }

    // Notification bookkeeping (D13) — last emission
    public int FinanceRecipientsResolvedCount { get; set; }
    public int FinanceRecipientsWithEmailCount { get; set; }
    public int FinanceEmailsQueuedCount { get; set; }
    /// <summary>QUEUED | NO_ELIGIBLE_FINANCE_USERS | ELIGIBLE_USERS_WITHOUT_EMAIL | NO_PLANT_IN_APP_ONLY.</summary>
    public string? FinanceEmailPolicyOutcome { get; set; }
    public bool BuyerRecipientResolved { get; set; }
    public bool BuyerEmailQueued { get; set; }
    /// <summary>Outbox state per recipient of the last emission (delivery is the worker's later fact).</summary>
    public List<NotificationDeliveryDto> Delivery { get; set; } = new();
    public Guid? LastNotificationCorrelationId { get; set; }
    public DateTime? LastNotifiedAtUtc { get; set; }
    public int NotificationResendCount { get; set; }
    /// <summary>Human explanation when Finance could not be addressed (e.g. no Finance user scoped to the plant).</summary>
    public string? NotificationWarning { get; set; }

    public string? RowVersion { get; set; }
    public List<PoGroupCancellationItemDto> Items { get; set; } = new();
    public List<PoGroupCancellationEcfEventDto> Events { get; set; } = new();
    public PoGroupCancellationActionsDto Actions { get; set; } = new();
}

/// <summary>v2.249.4 — a Finance review that was superseded by a later Buyer declaration (history, labelled as such).</summary>
public class PoGroupCancellationPreviousReviewDto
{
    /// <summary>CONFIRMED | REJECTED.</summary>
    public string Status { get; set; } = string.Empty;
    public string? ByName { get; set; }
    public DateTime? AtUtc { get; set; }
    public string? Note { get; set; }
}

public class NotificationDeliveryDto
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
}

public class PoGroupCancellationResultDto
{
    public Guid CancellationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string RequestStatusCode { get; set; } = string.Empty;
    public bool RequestCancelled { get; set; }
    public bool FinanceConfirmationRequired { get; set; }
    public int FinanceRecipientsResolvedCount { get; set; }
    public int ReleasedLineCount { get; set; }
    public int ClosedLineCount { get; set; }
    public int VoidedPoAttachments { get; set; }
    public int CancelledPlannedPayments { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NotificationWarning { get; set; }
}

/// <summary>Read-side eligibility of the cancel action for one group and the current caller.</summary>
public class PoGroupCancellationEligibilityDto
{
    public bool CanCancel { get; set; }
    public string? BlockCode { get; set; }
    public string? BlockMessage { get; set; }
    /// <summary>True when the group ever had a P.O.: the dialog must collect the ECF declaration.</summary>
    public bool FinanceConfirmationWouldBeRequired { get; set; }
    public string? ActorAuthorizationBlockCode { get; set; }
}

/// <summary>D4 — unconfirmed prior cancellations that block a replacement P.O. on a group (or on a set of lines).</summary>
public class PriorEcfConfirmationDto
{
    public bool Blocked { get; set; }
    public List<PriorEcfBlockingCancellationDto> Cancellations { get; set; } = new();
    public string? Message { get; set; }
}

public class PriorEcfBlockingCancellationDto
{
    public Guid CancellationId { get; set; }
    public Guid PreviousPoGroupId { get; set; }
    public string? PreviousPoNumber { get; set; }
    public string? PreviousSupplierName { get; set; }
    public string FinanceConfirmationStatus { get; set; } = string.Empty;
    public string EcfStatus { get; set; } = string.Empty;
    public string? FinanceConfirmationNote { get; set; }
    public List<int> LineNumbers { get; set; } = new();
}

/// <summary>Last Finance return of a group: from the new columns when present, otherwise from history (kind unclassified).</summary>
public class LastFinanceReturnDto
{
    /// <summary>PO_CORRECTION | ORDER_CANCELLATION_REQUESTED | null (legacy, not classified — never inferred).</summary>
    public string? Kind { get; set; }
    public string KindLabel { get; set; } = string.Empty;
    public string? ReasonCode { get; set; }
    /// <summary>The human reason as typed by Finance (from history "Motivo:" text when columns are absent).</summary>
    public string? Notes { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public string? ReturnedByName { get; set; }
    /// <summary>COLUMNS | HISTORY.</summary>
    public string Source { get; set; } = "HISTORY";
}

/// <summary>Finance queue row: cancellations whose ECF confirmation is pending or rejected.</summary>
public class FinancePoCancellationListItemDto
{
    public Guid CancellationId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string? RequestTitle { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public string? SupplierName { get; set; }
    public string? CompanyName { get; set; }
    public string? PlantName { get; set; }
    public int? PlantId { get; set; }
    public decimal TotalAmountSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public string? PurchaseOrderNumberSnapshot { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string EcfStatus { get; set; } = string.Empty;
    public string? EcfReference { get; set; }
    public string? EcfDeclaredByName { get; set; }
    public DateTime? EcfDeclaredAtUtc { get; set; }
    public string FinanceConfirmationStatus { get; set; } = string.Empty;
    public string? FinanceConfirmationNote { get; set; }
    /// <summary>v2.249.4 — see PoGroupCancellationDto.FinanceReviewAppliesToCurrentDeclaration.</summary>
    public bool FinanceReviewAppliesToCurrentDeclaration { get; set; }
    /// <summary>v2.249.4 — the Buyer re-declared after this review; shown to the reviewer as context, never as the current decision.</summary>
    public PoGroupCancellationPreviousReviewDto? PreviousFinanceReview { get; set; }
    public DateTime CancelledAtUtc { get; set; }
    public string? CancelledByName { get; set; }
    public int FinanceRecipientsResolvedCount { get; set; }
    public string? FinanceEmailPolicyOutcome { get; set; }
    public string? RowVersion { get; set; }
    /// <summary>Whether THIS caller may confirm/reject (Finance role + plant scope + not the declarer).</summary>
    public bool CanReview { get; set; }
    public string? ReviewBlockReason { get; set; }
}

public class FinancePoCancellationListDto
{
    public int PendingCount { get; set; }
    public int RejectedCount { get; set; }
    public int ConfirmedCount { get; set; }
    public List<FinancePoCancellationListItemDto> Items { get; set; } = new();
}

/// <summary>Request-level active approved figures (D5): per currency, never summed across currencies.</summary>
public class ActiveApprovedTotalsDto
{
    public List<CurrencyAmountDto> ActiveByCurrency { get; set; } = new();
    /// <summary>Single scalar only when exactly one currency is active; otherwise null.</summary>
    public decimal? ActiveAmount { get; set; }
    /// <summary>The single active currency, or "MULTIPLE", or null when nothing is active.</summary>
    public string? ActiveCurrency { get; set; }
    public decimal? HistoricalApprovedTotalAmount { get; set; }
    public string? HistoricalApprovedCurrencyCode { get; set; }
    public int CancelledGroupCount { get; set; }
    public int ReleasedItemCount { get; set; }
}
