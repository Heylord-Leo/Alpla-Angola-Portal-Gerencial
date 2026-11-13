using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// A supplier's credit account with ONE buying company in ONE currency (D1: supplier × company × currency;
/// Viana 1 and Viana 2 share the APA balance, APS has its own). The plant of each movement is kept on the
/// entry for traceability only. Balance, reserved and available are never stored: they are derived from
/// the entries and the live reservations (see <c>SupplierCreditPolicy</c>).
/// </summary>
public class SupplierCreditAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>ISO code snapshot (AOA, USD, EUR …) — one currency per account, never converted.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>CCS-000001 … allocated from SystemCounters; unique.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>ACTIVE | CLOSED.</summary>
    public string Status { get; set; } = "ACTIVE";

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<SupplierCreditEntry> Entries { get; set; } = new List<SupplierCreditEntry>();
    public ICollection<SupplierCreditReservation> Reservations { get; set; } = new List<SupplierCreditReservation>();
}

/// <summary>
/// One append-only ledger movement. Inflows are positive (CREDIT_IN, ADJUSTMENT_IN, REVERSAL), outflows
/// negative (ADJUSTMENT_OUT, APPLICATION, CLOSE_OUT). Rows are never updated or deleted; a mistake is
/// corrected by a further entry. Reservations are HOLDS and write no entry until applied.
/// </summary>
public class SupplierCreditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }
    public SupplierCreditAccount Account { get; set; } = null!;

    /// <summary>SupplierCreditConstants.EntryTypes.</summary>
    public string EntryType { get; set; } = string.Empty;

    /// <summary>Signed movement (positive inflow, negative outflow), 2 decimals.</summary>
    public decimal SignedAmount { get; set; }

    /// <summary>Account balance after this entry (audit snapshot; the authoritative balance is Σ SignedAmount).</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>Supplier-side document reference (credit note, agreement, e-mail…). Mandatory for Purchasing entries.</summary>
    public string? Reference { get; set; }
    public DateTime? DocumentDateUtc { get; set; }

    /// <summary>Human reason / justification (mandatory for adjustments and reversals).</summary>
    public string Reason { get; set; } = string.Empty;

    // ── Workflow linkage (APPLICATION / REVERSAL) ──
    public Guid? ReservationId { get; set; }
    public SupplierCreditReservation? Reservation { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? RequestPoGroupId { get; set; }
    /// <summary>SupplierCreditConstants.Stages for APPLICATION / REVERSAL entries.</summary>
    public string? Stage { get; set; }
    public string? RequestNumberSnapshot { get; set; }
    public string? PurchaseOrderNumberSnapshot { get; set; }
    /// <summary>Plant of the request / group the movement relates to (traceability only; never part of the key).</summary>
    public int? OriginPlantId { get; set; }

    /// <summary>APPLY:{ReservationId}:{Stage} | REV:{ReservationId} | ENTRY:{clientKey}. Filtered unique.</summary>
    public string? IdempotencyKey { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A hold of part of an account's balance for ONE P.O. group. At most one live reservation (RESERVED,
/// PARTIALLY_APPLIED, APPLIED) per group. The split between the advance and the balance stage is computed
/// once at reservation (recomputed on correction) and persisted, never re-derived later, so a reversal can
/// restore the original schedule. <c>AppliedAmount</c> grows only through stage applications.
/// </summary>
public class SupplierCreditReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }
    public SupplierCreditAccount Account { get; set; } = null!;

    public Guid RequestId { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup RequestPoGroup { get; set; } = null!;

    /// <summary>C — the coverage reserved for the group (≤ group total).</summary>
    public decimal Amount { get; set; }
    /// <summary>Part of C applied at the advance stage (Antecipado Parcial); = Amount for 100 % Antecipado; 0 for Pós-Pago.</summary>
    public decimal AdvanceCreditAmount { get; set; }
    /// <summary>Part of C applied at the balance stage; = Amount for Pós-Pago; 0 for 100 % Antecipado.</summary>
    public decimal BalanceCreditAmount { get; set; }
    /// <summary>Σ applied so far (0 … Amount).</summary>
    public decimal AppliedAmount { get; set; }

    /// <summary>SupplierCreditConstants.ReservationStatuses.</summary>
    public string Status { get; set; } = "RESERVED";

    public Guid? AdvanceAppliedEntryId { get; set; }
    public Guid? BalanceAppliedEntryId { get; set; }

    /// <summary>Hold that was released without a ledger entry (release, reversal, or a balance settled below the hold).</summary>
    public decimal? ReleasedHoldAmount { get; set; }
    /// <summary>Applied amount returned to the account by the REVERSAL entry.</summary>
    public decimal? ReversedAppliedAmount { get; set; }
    /// <summary>SupplierCreditConstants.ReversalKinds.</summary>
    public string? ReversalKind { get; set; }
    /// <summary>SupplierCreditConstants.ReleaseReasons.</summary>
    public string? ReleaseReason { get; set; }
    public string? ReleaseNote { get; set; }
    /// <summary>A correction that CHANGED the amount supersedes this reservation with a new one.</summary>
    public Guid? SupersededByReservationId { get; set; }
    /// <summary>D7: the PLANNED bank rows recreated by a reversal.</summary>
    public int? ReopenedAdvancePaymentId { get; set; }
    public int? ReopenedBalancePaymentId { get; set; }

    public Guid ReservedByUserId { get; set; }
    public DateTime ReservedAtUtc { get; set; }
    public Guid? AppliedByUserId { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? ReversalReason { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
