using System.Collections.Generic;

namespace AlplaPortal.Application.Interfaces.Finance;

/// <summary>
/// Single source of truth for which finance actions (SCHEDULE, PAY, RETURN, ADD_NOTE, ADD_PROOF)
/// are available for a request, given the same effective state the mutation endpoints
/// (FinanceController.SchedulePayment/MarkAsPaid/ReturnForAdjustment) themselves check.
///
/// GetPayments (listing) and the mutation endpoints (execution) must both go through this
/// service - never through independently-maintained copies of the same status checks - so the
/// list can never advertise an action the corresponding endpoint would reject, and vice versa.
/// </summary>
public interface IFinancePaymentEligibilityService
{
    /// <summary>Computes the full action list (and, for actions absent from it, an internal-only unavailability reason) for one request row.</summary>
    FinanceActionEligibilityResult Evaluate(FinanceEligibilityInput input);

    /// <summary>
    /// Mirrors FinanceController.SchedulePayment's guard exactly. For QUOTATION, the group status
    /// remains the sole authority (unchanged). For PAYMENT, the group status is authoritative once
    /// it holds a genuine, non-legacy value; only when the group is null/empty/PENDING (never
    /// actively synced for older PAYMENT-type auto-created groups) does it fall back to
    /// requestStatusCode — and even then, only a small canonical parent-status set enables
    /// scheduling, never the same broad set MarkAsPaid trusts for CanPay.
    /// </summary>
    bool CanSchedule(string requestTypeCode, string requestStatusCode, string? groupStatus);

    /// <summary>Mirrors FinanceController.MarkAsPaid's guard exactly (branches on request type, same as the endpoint).</summary>
    bool CanPay(string requestTypeCode, string requestStatusCode, string? groupStatus);

    /// <summary>Mirrors FinanceController.ReturnForAdjustment's parent-status guard exactly. Retained
    /// for backward compatibility / legacy single-group requests; new code should prefer the
    /// group-scoped <see cref="CanReturnGroup"/>.</summary>
    bool CanReturn(string? requestStatusCode);

    /// <summary>
    /// Group-scoped return eligibility: whether THIS RequestPoGroup may be returned to the Buyer for
    /// P.O. correction, judged from the group's OWN status only (PO_ISSUED or PAYMENT_SCHEDULED). A
    /// sibling group's lifecycle (e.g. already PAYMENT_COMPLETED) never affects this answer, so a
    /// multi-group request can return one group without touching the others.
    /// </summary>
    bool CanReturnGroup(string? groupStatus);

    /// <summary>
    /// The financial-mutation actions (SCHEDULE, PAY, CANCEL_SCHEDULE, RETURN) available for ONE
    /// RequestPoGroup, derived exclusively from that group's own status. This is the per-group source
    /// of truth the multi-group UI and the mutation endpoints share: a paid sibling can never suppress
    /// or add an action here. ADD_NOTE / ADD_PROOF are request-level document actions and are NOT
    /// included — they are resolved by <see cref="Evaluate"/>.
    /// </summary>
    IReadOnlyList<string> EvaluateGroupActions(string requestTypeCode, string requestStatusCode, string? groupStatus);

    /// <summary>
    /// Mirrors FinanceController.CancelSchedule's guard exactly. Eligible only for a group whose
    /// OWN status is currently PAYMENT_SCHEDULED or ADVANCE_PAYMENT_SCHEDULED — never derived from
    /// the parent request status. No type-branching is needed: unlike CanSchedule's legacy-PENDING
    /// fallback, these two statuses are always genuinely written values (never a legacy default),
    /// so the group's own status is always authoritative here.
    /// </summary>
    bool CanCancelSchedule(string? groupStatus);
}

/// <summary>One RequestPoGroup's identity/status, as seen by the eligibility calculation.</summary>
public class FinancePoGroupEligibilityInput
{
    public System.Guid GroupId { get; init; }
    public string GroupStatus { get; init; } = string.Empty;
}

/// <summary>
/// Plain input model (not the EF entity) so eligibility can be unit-tested without constructing
/// fully-wired Request/RequestPoGroup entities, and so the same input shape can be built once
/// from already-loaded data in GetPayments, SchedulePayment, MarkAsPaid, and ReturnForAdjustment.
/// </summary>
public class FinanceEligibilityInput
{
    public string RequestTypeCode { get; init; } = string.Empty;
    public string RequestStatusCode { get; init; } = string.Empty;
    public bool IsPaid { get; init; }
    public bool HasProof { get; init; }
    public IReadOnlyList<FinancePoGroupEligibilityInput> PoGroups { get; init; } = System.Array.Empty<FinancePoGroupEligibilityInput>();
}

public class FinanceActionEligibilityResult
{
    public IReadOnlyList<string> Actions { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Internal-only reason codes for actions NOT present in Actions (e.g. "SCHEDULE" -> "NO_GROUP_IN_SCHEDULABLE_STATUS").
    /// Not mapped onto the public FinanceListItemDto - kept for logging/tests/troubleshooting only.
    /// </summary>
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } = new Dictionary<string, string>();
}

/// <summary>Action codes returned in FinanceListItemDto.AvailableFinanceActions. Kept identical to the historical string literals.</summary>
public static class FinancePaymentActionCodes
{
    public const string Schedule = "SCHEDULE";
    public const string Pay = "PAY";
    public const string Return = "RETURN";
    public const string AddNote = "ADD_NOTE";
    public const string AddProof = "ADD_PROOF";
    public const string CancelSchedule = "CANCEL_SCHEDULE";
}

/// <summary>Internal-only reason codes explaining why an action was omitted from AvailableFinanceActions.</summary>
public static class FinancePaymentUnavailableReasons
{
    public const string NoPoGroup = "NO_PO_GROUP";
    public const string NoGroupInSchedulableState = "NO_GROUP_IN_SCHEDULABLE_STATE";
    public const string ParentOrGroupStatusNotEligibleForPay = "PARENT_OR_GROUP_STATUS_NOT_ELIGIBLE_FOR_PAY";
    public const string ParentStatusNotEligibleForReturn = "PARENT_STATUS_NOT_ELIGIBLE_FOR_RETURN";
    public const string AlreadyPaid = "ALREADY_PAID";
    public const string NoGroupCurrentlyScheduled = "NO_GROUP_CURRENTLY_SCHEDULED";
}
