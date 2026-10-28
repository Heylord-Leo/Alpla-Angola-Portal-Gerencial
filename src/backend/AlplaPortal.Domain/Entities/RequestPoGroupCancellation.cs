using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// The durable record of ONE P.O. group cancellation after final approval (phase 1). One row per group
/// (<see cref="RequestPoGroupId"/> unique); the group itself is set to CANCELLED and never mutated again.
///
/// Two human statements live here and are deliberately separate: the Buyer's DECLARATION about the external
/// ECF/P.O. document (<see cref="EcfStatus"/>, <see cref="EcfDeclaredByUserId"/>) and Finance's CONFIRMATION of its
/// annulment (<see cref="FinanceConfirmationStatus"/>, <see cref="FinanceConfirmedByUserId"/>). The Portal never
/// verifies Primavera: both are attestations with actor and timestamp, and every change is appended to
/// <see cref="EcfEvents"/>.
/// </summary>
public class RequestPoGroupCancellation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup RequestPoGroup { get; set; } = null!;

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>RESTART_QUOTATION | CANCEL_ITEMS (<see cref="PoGroupCancellationConstants.Kinds"/>).</summary>
    public string Kind { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    // ── Snapshots of the group at cancellation (the group row keeps its own values; these are the frozen evidence) ──
    public string PreviousGroupStatus { get; set; } = string.Empty;
    public string? PurchaseOrderNumberSnapshot { get; set; }
    public decimal TotalAmountSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public int? ApprovalBatchNumberSnapshot { get; set; }

    /// <summary>The FINANCE_RETURN_ADJUSTMENT history row that preceded this cancellation, when there was one.</summary>
    public Guid? OriginFinanceReturnHistoryId { get; set; }

    public Guid CancelledByUserId { get; set; }
    public DateTime CancelledAtUtc { get; set; }

    // ── Buyer declaration about the external ECF ──
    /// <summary>True when the group ever had a registered P.O. (number, PO attachment or REGISTER_PO history).</summary>
    public bool FinanceConfirmationRequired { get; set; }
    public string EcfStatus { get; set; } = PoGroupCancellationConstants.EcfStatuses.NotApplicable;
    public string? EcfReference { get; set; }
    public string? EcfDeclarationNote { get; set; }
    public Guid? EcfEvidenceAttachmentId { get; set; }
    public Guid? EcfDeclaredByUserId { get; set; }
    public DateTime? EcfDeclaredAtUtc { get; set; }

    // ── Finance confirmation (separate actor, separate timestamp) ──
    public string FinanceConfirmationStatus { get; set; } = PoGroupCancellationConstants.FinanceConfirmationStatuses.NotRequired;
    public Guid? FinanceConfirmedByUserId { get; set; }
    public DateTime? FinanceConfirmedAtUtc { get; set; }
    public string? FinanceConfirmationNote { get; set; }
    /// <summary>Last Finance review (confirmation OR rejection).</summary>
    public Guid? FinanceReviewedByUserId { get; set; }
    public DateTime? FinanceReviewedAtUtc { get; set; }

    // ── Notification bookkeeping (D13): what the LAST emission resolved ──
    public int FinanceRecipientsResolvedCount { get; set; }
    /// <summary>Finance users with a non-blank e-mail address among the eligible ones (last emission).</summary>
    public int FinanceRecipientsWithEmailCount { get; set; }
    /// <summary>Finance e-mails queued to the outbox by the last emission (policy: plant-scoped Finance always gets e-mail).</summary>
    public int FinanceEmailsQueuedCount { get; set; }
    /// <summary>QUEUED | NO_ELIGIBLE_FINANCE_USERS | ELIGIBLE_USERS_WITHOUT_EMAIL | NO_PLANT_IN_APP_ONLY (see PoCancellationRecipientOutcome).</summary>
    public string? FinanceEmailPolicyOutcome { get; set; }
    public bool BuyerRecipientResolved { get; set; }
    public bool BuyerEmailQueued { get; set; }
    public Guid? LastNotificationCorrelationId { get; set; }
    public DateTime? LastNotifiedAtUtc { get; set; }
    public int NotificationResendCount { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<RequestPoGroupCancellationItem> Items { get; set; } = new List<RequestPoGroupCancellationItem>();
    public ICollection<RequestPoGroupCancellationEcfEvent> EcfEvents { get; set; } = new List<RequestPoGroupCancellationEcfEvent>();
}

/// <summary>
/// One affected line item of a cancellation. The replacement-P.O. dependency follows the LINE, not the group or
/// the batch: whichever new batch/group the line lands in, the guard finds every unconfirmed prior cancellation
/// through these rows (multiple restart cycles → several rows per line).
/// </summary>
public class RequestPoGroupCancellationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestPoGroupCancellationId { get; set; }
    public RequestPoGroupCancellation Cancellation { get; set; } = null!;

    public Guid RequestLineItemId { get; set; }
    public RequestLineItem RequestLineItem { get; set; } = null!;

    public Guid PreviousPoGroupId { get; set; }
    public Guid? ApprovalBatchItemId { get; set; }

    /// <summary>RELEASED (back to quotation) | CLOSED (cancelled after approval).</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? PreviousLineItemStatusCode { get; set; }
    public string? PreviousQuotationLifecycleStatus { get; set; }
    public Guid? PreviousSelectedQuotationItemId { get; set; }
}

/// <summary>Append-only audit of every human statement about the external ECF (declaration, update, confirmation, rejection, resend).</summary>
public class RequestPoGroupCancellationEcfEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestPoGroupCancellationId { get; set; }
    public RequestPoGroupCancellation Cancellation { get; set; } = null!;

    /// <summary><see cref="PoGroupCancellationConstants.EcfEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;
    public string EcfStatusAfter { get; set; } = string.Empty;
    public string FinanceStatusAfter { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public Guid? EvidenceAttachmentId { get; set; }
    public Guid ActorUserId { get; set; }
    /// <summary>The role under which the actor acted (BUYER | FINANCE | SYSTEM_ADMINISTRATOR), frozen for the audit.</summary>
    public string ActorRoleSnapshot { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}
