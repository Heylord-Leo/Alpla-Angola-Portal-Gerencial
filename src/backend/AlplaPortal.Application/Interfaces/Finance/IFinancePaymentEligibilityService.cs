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

    /// <summary>Mirrors FinanceController.SchedulePayment's group-status guard exactly.</summary>
    bool CanSchedule(string? groupStatus);

    /// <summary>Mirrors FinanceController.MarkAsPaid's guard exactly (branches on request type, same as the endpoint).</summary>
    bool CanPay(string requestTypeCode, string requestStatusCode, string? groupStatus);

    /// <summary>Mirrors FinanceController.ReturnForAdjustment's parent-status guard exactly.</summary>
    bool CanReturn(string? requestStatusCode);
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
}

/// <summary>Internal-only reason codes explaining why an action was omitted from AvailableFinanceActions.</summary>
public static class FinancePaymentUnavailableReasons
{
    public const string NoPoGroup = "NO_PO_GROUP";
    public const string NoGroupInSchedulableState = "NO_GROUP_IN_SCHEDULABLE_STATE";
    public const string ParentOrGroupStatusNotEligibleForPay = "PARENT_OR_GROUP_STATUS_NOT_ELIGIBLE_FOR_PAY";
    public const string ParentStatusNotEligibleForReturn = "PARENT_STATUS_NOT_ELIGIBLE_FOR_RETURN";
    public const string AlreadyPaid = "ALREADY_PAID";
}
