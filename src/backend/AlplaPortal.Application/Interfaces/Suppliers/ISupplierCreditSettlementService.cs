using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces.Suppliers;

/// <summary>
/// Workflow side of supplier credit: reservation at P.O. registration, application by Finance, release on
/// cancellation / abandonment and reversal. EVERY method runs INSIDE the caller's open transaction (RegisterPo,
/// MarkAsPaid, ConfirmAdvancePayment, apply-credit, cancellation …) and never commits: the caller's single
/// SaveChanges + Commit persists the credit facts together with the group / payment facts. Lock order inside:
/// (1) group row [caller] → (2) account row → (3) the group's reservations → (4) the caller's other locks.
/// </summary>
public interface ISupplierCreditSettlementService
{
    /// <summary>What the P.O. wizard may offer for this group (flag, account, availability, current hold).</summary>
    Task<SupplierCreditEligibilityDto> EvaluateEligibilityAsync(
        Request request, RequestPoGroup group, string? paymentConditionCode, bool actorMayReserve, CancellationToken ct = default);

    /// <summary>
    /// Reserve <paramref name="requestedAmount"/> for the group (first registration) or apply a correction decision
    /// (KEEP | CHANGE | REMOVE) to its live hold. Writes the reservation, the group pointer / coverage and the owed
    /// PLANNED rows (bank + SUPPLIER_CREDIT) for the registered terms. Returns the owed rows it created so the caller
    /// keeps its own status / history logic.
    /// </summary>
    Task<SupplierCreditOperationOutcome<ReservationOutcome>> ReserveOrCorrectAsync(
        ReserveSupplierCreditCommand cmd, CancellationToken ct = default);

    /// <summary>
    /// Apply the PLANNED SUPPLIER_CREDIT row of <paramref name="stage"/>: ledger APPLICATION entry, reservation
    /// AppliedAmount / status, row COMPLETED. Idempotent by row status + reservation pointer + idempotency key.
    /// </summary>
    Task<SupplierCreditOperationOutcome<ApplicationOutcome>> ApplyStageAsync(
        Request request, RequestPoGroup group, string stage, Guid actorUserId, string? note, CancellationToken ct = default);

    /// <summary>Release a RESERVED hold (cancellation, correction removal, abandoned request). No ledger entry.</summary>
    Task<SupplierCreditOperationOutcome<ReleaseOutcome>> ReleaseAsync(
        RequestPoGroup group, string releaseReason, string? note, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// D7 reversal (stage 1: FULL_COVERAGE only). REVERSAL entry = applied amount; reservation REVERSED; credit rows
    /// CANCELLED; the bank obligation reopened as PLANNED rows per the registered terms; group status reopened.
    /// </summary>
    Task<SupplierCreditOperationOutcome<ReversalOutcome>> ReverseAsync(
        Request request, RequestPoGroup group, Guid actorUserId, string reason, CancellationToken ct = default);

    /// <summary>The live reservation of a group, or null.</summary>
    Task<SupplierCreditReservation?> GetLiveReservationAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Account figures after the caller's pending changes (same context, no commit).</summary>
    Task<(decimal Balance, decimal Reserved, decimal Available)> AccountFiguresAsync(Guid accountId, CancellationToken ct = default);
}

public sealed class ReserveSupplierCreditCommand
{
    public required Request Request { get; init; }
    public required RequestPoGroup Group { get; init; }
    public required string PaymentConditionCode { get; init; }
    public decimal AdvancePercent { get; init; }
    /// <summary>First registration: the Buyer asked to use credit.</summary>
    public bool UseSupplierCredit { get; init; }
    /// <summary>Requested coverage (first registration, or CHANGE).</summary>
    public decimal? RequestedAmount { get; init; }
    /// <summary>Correction: KEEP | CHANGE | REMOVE (null on a first registration).</summary>
    public string? CorrectionAction { get; init; }
    public bool IsCorrection { get; init; }
    public bool NewReservationsEnabled { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorName { get; init; }
}

public sealed class ReservationOutcome
{
    /// <summary>The live reservation after the operation (null when none — bank-only registration).</summary>
    public SupplierCreditReservation? Reservation { get; init; }
    public decimal Coverage { get; init; }
    public bool IsFullCoverage { get; init; }
    /// <summary>PLANNED owed rows created by this registration (bank + credit).</summary>
    public List<RequestPayment> OwedRows { get; init; } = new();
    /// <summary>History action written (SUPPLIER_CREDIT_RESERVED / _CHANGED / _RELEASED) or null when nothing credit-related changed.</summary>
    public string? HistoryAction { get; init; }
    public string? HistoryText { get; init; }
    /// <summary>Account figures after the operation (null when no account was touched).</summary>
    public decimal? AccountAvailableAfter { get; init; }
}

public sealed class ApplicationOutcome
{
    public required SupplierCreditReservation Reservation { get; init; }
    public required RequestPayment CreditRow { get; init; }
    public required SupplierCreditEntry Entry { get; init; }
    public decimal AppliedAmount { get; init; }
    public decimal ReleasedHoldAmount { get; init; }
    public decimal AccountBalanceAfter { get; init; }
    public decimal AccountAvailableAfter { get; init; }
    public string HistoryText { get; init; } = string.Empty;
}

public sealed class ReleaseOutcome
{
    public required SupplierCreditReservation Reservation { get; init; }
    public decimal ReleasedHoldAmount { get; init; }
    public decimal AccountAvailableAfter { get; init; }
    public string HistoryText { get; init; } = string.Empty;
}

public sealed class ReversalOutcome
{
    public required SupplierCreditReservation Reservation { get; init; }
    public required SupplierCreditEntry Entry { get; init; }
    public string Kind { get; init; } = string.Empty;
    public decimal ReversedAmount { get; init; }
    public decimal ReleasedHoldAmount { get; init; }
    public decimal AccountAvailableAfter { get; init; }
    public List<RequestPayment> ReopenedRows { get; init; } = new();
    public List<RequestPayment> CancelledCreditRows { get; init; } = new();
    /// <summary>The group status the reversal reopens to (PO_ISSUED for Pós-Pago, ADVANCE_PAYMENT_REQUIRED for 100 % Antecipado).</summary>
    public string ReopenedGroupStatus { get; init; } = string.Empty;
    public string HistoryText { get; init; } = string.Empty;
}
