using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Buyer self-takeover of a QUOTATION request assigned to another Buyer (docs/BUYER_WORKSPACE_QUOTATION_ACTIONS.md §8).
/// Shared by the endpoint, the notification orchestrator and the tests so there is ONE interpretation.
/// </summary>
public static class BuyerTakeOverPolicy
{
    public const int JustificationMaxLength = 500;

    /// <summary>History action written by a takeover (previous and new Buyer plus the justification in the comment).</summary>
    public const string HistoryAction = "COMPRADOR_TRANSFERIDO";

    /// <summary>Request field-change audit row written by a takeover.</summary>
    public const string FieldChangeName = "BuyerId";
    public const string FieldChangeDisplayName = "Comprador responsável";

    /// <summary>Statuses in which a takeover is refused (backend and both UIs).</summary>
    public static readonly IReadOnlyList<string> TerminalStatuses = new[]
    {
        RequestConstants.Statuses.Cancelled,
        RequestConstants.Statuses.Rejected,
        RequestConstants.Statuses.Completed,
        RequestConstants.Statuses.PaymentCompleted,
    };

    public static bool IsTerminal(string? statusCode) => statusCode != null && TerminalStatuses.Contains(statusCode, StringComparer.Ordinal);

    /// <summary>Batch / request statuses that mean an AREA decision is pending (pending area managers are notified via the routing resolver).</summary>
    public static readonly IReadOnlyList<string> PendingAreaStatuses = new[] { RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, RequestConstants.ApprovalBatchStatuses.AreaAdjustment };

    /// <summary>Batch / request statuses that mean a FINAL decision is pending (the request's or the company's final-approver nominee is notified via the routing resolver).</summary>
    public static readonly IReadOnlyList<string> PendingFinalStatuses = new[] { RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, RequestConstants.ApprovalBatchStatuses.FinalAdjustment };

    /// <summary>
    /// History actions whose ACTOR counts as a request participant for the transfer notice — explicit, by action
    /// code, never by text matching. Approval decisions (batch and legacy), P.O. registration, receiving, finance
    /// actions and quotation reuse authorisations. Informational rows (observations, data edits, status syncs,
    /// quotation edits) are deliberately excluded.
    /// </summary>
    public static readonly IReadOnlyList<string> ParticipantHistoryActions = new[]
    {
        "BATCH_AREA_APPROVED", "BATCH_AREA_REJECTED", "BATCH_AREA_ADJUSTMENT",
        "BATCH_FINAL_APPROVED", "BATCH_FINAL_REJECTED", "BATCH_FINAL_ADJUSTMENT",
        WorkflowEventCodes.AreaApproved, WorkflowEventCodes.AreaRejected, WorkflowEventCodes.FinalApproved, WorkflowEventCodes.FinalRejected,
        WorkflowEventCodes.QuotationItemAwarded,
        "REGISTER_PO", "REREGISTER_PO", "PO_GROUP_ACTIVATED",
        "CONFIRM_RECEIVING", "RECEIVING_PROGRESS", "RECEIVING_REOPENED",
        "FINANCE_RETURN_ADJUSTMENT", "FINANCIAL_CORRECTION", "NOTA_FINANCEIRA", "PAYMENT_COMPLETED", "SCHEDULE_ADVANCE", "ADVANCE_PAYMENT_COMPLETED",
        "QUOTATION_REUSE_AUTHORIZED",
    };

    // ProblemDetails codes
    public const string CodeNotApplicable = "TAKEOVER_NOT_APPLICABLE";          // PAYMENT request
    public const string CodeRoleRequired = "TAKEOVER_BUYER_ROLE_REQUIRED";
    public const string CodeTerminalStatus = "TAKEOVER_TERMINAL_STATUS";
    public const string CodeJustificationRequired = "TAKEOVER_JUSTIFICATION_REQUIRED";
    public const string CodeJustificationTooLong = "TAKEOVER_JUSTIFICATION_TOO_LONG";
    public const string CodeExpectedOwnerMissing = "TAKEOVER_EXPECTED_OWNER_MISSING";   // field omitted
    public const string CodeExpectedOwnerNull = "TAKEOVER_EXPECTED_OWNER_NULL";         // explicit null: not a takeover, use the claim
    public const string CodeExpectedOwnerInvalid = "TAKEOVER_EXPECTED_OWNER_INVALID";
    public const string CodeNotAssigned = "TAKEOVER_NOT_ASSIGNED";                      // request has no Buyer now
    public const string CodeStale = "TAKEOVER_STALE";                                   // owner changed since the confirmation was shown
    public const string CodeConcurrent = "TAKEOVER_CONCURRENT";                         // lost the RowVersion race
    public const string CodeAlreadyOwner = "TAKEOVER_ALREADY_OWNER";                    // idempotent 200
}
