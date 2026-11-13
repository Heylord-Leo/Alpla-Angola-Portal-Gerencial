using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.SupplierCredit;

/// <summary>One credit account of a supplier with a buying company in a currency, with its derived figures.</summary>
public class SupplierCreditAccountDto
{
    public Guid Id { get; set; }
    public int SupplierId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyCode { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>Σ ledger entries.</summary>
    public decimal Balance { get; set; }
    /// <summary>Σ holds of RESERVED / PARTIALLY_APPLIED reservations.</summary>
    public decimal Reserved { get; set; }
    /// <summary>Balance − Reserved.</summary>
    public decimal Available { get; set; }
    public int LiveReservationCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateSupplierCreditAccountDto
{
    public int CompanyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>A Purchasing ledger entry (CREDIT_IN, ADJUSTMENT_IN, ADJUSTMENT_OUT).</summary>
public class AddSupplierCreditEntryDto
{
    public string EntryType { get; set; } = string.Empty;
    /// <summary>Absolute amount (the sign is derived from the entry type).</summary>
    public decimal Amount { get; set; }
    /// <summary>Supplier document reference — mandatory.</summary>
    public string? Reference { get; set; }
    public DateTime? DocumentDateUtc { get; set; }
    /// <summary>Reason / justification — mandatory (min 10 chars).</summary>
    public string? Reason { get; set; }
    /// <summary>Optional client idempotency key (ENTRY:{key}) so a retried submission never doubles the credit.</summary>
    public string? ClientKey { get; set; }
}

/// <summary>Unified movement feed of an account: ledger entries AND reservation events (holds are not money, but they explain Available).</summary>
public class SupplierCreditMovementDto
{
    public Guid Id { get; set; }
    /// <summary>ENTRY | RESERVATION.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>EntryTypes.* for entries; the reservation event (RESERVED, PARTIALLY_APPLIED, APPLIED, RELEASED, REVERSED, SUPERSEDED) for holds.</summary>
    public string Type { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    /// <summary>Signed ledger amount (entries) — null for reservation events.</summary>
    public decimal? SignedAmount { get; set; }
    /// <summary>Hold amount for reservation events (current hold after the event).</summary>
    public decimal? HoldAmount { get; set; }
    public decimal? AppliedAmount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
    public string? Stage { get; set; }
    public Guid? RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public Guid? RequestPoGroupId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? PlantName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? ActorName { get; set; }
}

/// <summary>The live reservation of a P.O. group, as shown on the group / Finance queue / wizard.</summary>
public class SupplierCreditReservationSummaryDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AdvanceCreditAmount { get; set; }
    public decimal BalanceCreditAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    /// <summary>Amount − AppliedAmount while holding; 0 otherwise.</summary>
    public decimal HoldAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime ReservedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? ReversalKind { get; set; }
    public string? ReleaseReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

/// <summary>GET …/po-groups/{groupId}/credit-eligibility — what the P.O. wizard may offer for THIS group.</summary>
public class SupplierCreditEligibilityDto
{
    /// <summary>The activation flag: new or changed reservations are accepted only when true.</summary>
    public bool Enabled { get; set; }
    /// <summary>The caller may reserve credit on this group (role, scope, ownership, request type, status).</summary>
    public bool CanReserve { get; set; }
    /// <summary>Why not (ErrorCodes.*) when <see cref="CanReserve"/> is false.</summary>
    public string? ReasonCode { get; set; }
    public string? ReasonMessage { get; set; }
    /// <summary>Payment terms on which credit may be used in this stage.</summary>
    public List<string> SupportedPaymentConditions { get; set; } = new();
    public decimal GroupTotalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public int CompanyId { get; set; }
    /// <summary>The account that would be debited (supplier × request company × group currency), when it exists and is active.</summary>
    public SupplierCreditAccountDto? Account { get; set; }
    /// <summary>Available for a NEW reservation on this group: account Available + the group's own current hold (correction).</summary>
    public decimal AvailableForGroup { get; set; }
    /// <summary>Highest coverage the wizard may request: min(AvailableForGroup, GroupTotalAmount).</summary>
    public decimal MaxCoverage { get; set; }
    /// <summary>The group's current live reservation (correction keep / change / remove), or null.</summary>
    public SupplierCreditReservationSummaryDto? CurrentReservation { get; set; }
}

/// <summary>POST api/v1/finance/{id}/apply-credit.</summary>
public class ApplySupplierCreditDto
{
    public Guid RequestPoGroupId { get; set; }
    /// <summary>SINGLE (stage 1) | ADVANCE | BALANCE (stage 2). Defaults to the group's current stage when omitted.</summary>
    public string? Stage { get; set; }
    public string? Note { get; set; }
    /// <summary>Base64 RowVersion of the group the client displayed (optional; 409 when stale).</summary>
    public string? ExpectedRowVersion { get; set; }
}

/// <summary>POST api/v1/finance/{id}/reverse-credit.</summary>
public class ReverseSupplierCreditDto
{
    public Guid RequestPoGroupId { get; set; }
    /// <summary>Mandatory, 20–1000 characters.</summary>
    public string? Reason { get; set; }
    public string? ExpectedRowVersion { get; set; }
}

/// <summary>POST api/v1/requests/{id}/po-groups/{groupId}/credit-reservation/release.</summary>
public class ReleaseSupplierCreditReservationDto
{
    /// <summary>Mandatory, 20–1000 characters.</summary>
    public string? Reason { get; set; }
    public string? ExpectedGroupRowVersion { get; set; }
}

/// <summary>Outcome of an application / reversal / release, echoed to the client and used by tests.</summary>
public class SupplierCreditOperationResultDto
{
    public string Message { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;
    public decimal AppliedAmount { get; set; }
    public decimal? LedgerAmount { get; set; }
    public decimal? ReleasedHoldAmount { get; set; }
    public decimal AccountBalance { get; set; }
    public decimal AccountAvailable { get; set; }
    public string GroupStatus { get; set; } = string.Empty;
    public int? CompletedPaymentId { get; set; }
    public List<int> ReopenedPaymentIds { get; set; } = new();
}
