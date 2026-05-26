namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents an expected payment entry in a contract's obligation calendar.
/// The pivot entity between Contract and Request (via Request.ContractPaymentObligationId).
/// Status lifecycle: PENDING → REQUEST_CREATED → PAID.
/// </summary>
public class ContractPaymentObligation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>Sequential number within the contract: 1, 2, 3...</summary>
    public int SequenceNumber { get; set; }

    /// <summary>Human-readable label e.g. "Parcela 3/12", "Mensalidade Jan/2026".</summary>
    public string? Description { get; set; }

    /// <summary>Expected payment amount. decimal(18,2).</summary>
    public decimal ExpectedAmount { get; set; }

    /// <summary>Defaults to contract currency if null.</summary>
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    // ── Due Date Tracking (DEC-117) ──────────────────────────────────────────────────

    /// <summary>
    /// The reference date used as the starting point for due date calculation.
    /// Populated from the contract's ReferenceEventTypeCode at obligation creation time.
    /// </summary>
    public DateTime? ReferenceDateUtc { get; set; }

    /// <summary>
    /// The due date as computed by ContractDueDateCalculator from the contract's payment rule.
    /// Always reflects the system's calculation, even when DueDateUtc has been manually overridden.
    /// </summary>
    public DateTime? CalculatedDueDateUtc { get; set; }

    /// <summary>
    /// The effective due date actually used for this obligation.
    /// Equals CalculatedDueDateUtc when auto-computed, or the user-provided date when overridden.
    /// Determines NeedByDateUtc on the generated payment request.
    /// </summary>
    public DateTime DueDateUtc { get; set; }

    /// <summary>
    /// Tracks how DueDateUtc was determined.
    /// AUTO_FROM_CONTRACT = system-calculated | MANUAL_OVERRIDE = user-set.
    /// Null for obligations created before this feature was deployed.
    /// </summary>
    public string? DueDateSourceCode { get; set; }

    /// <summary>
    /// Date until which no late penalty applies (inclusive).
    /// GraceDate = DueDate + contract.GracePeriodDays.
    /// Equals DueDateUtc when GracePeriodDays is null or 0.
    /// </summary>
    public DateTime? GraceDateUtc { get; set; }

    /// <summary>
    /// First day on which late penalty/interest begins accruing.
    /// PenaltyStartDate = GraceDate + 1 day.
    /// </summary>
    public DateTime? PenaltyStartDateUtc { get; set; }

    // ── Operational Fields ───────────────────────────────────────────────────────────

    /// <summary>Date the supplier invoice for this obligation was received. Used as reference when ReferenceEventTypeCode = INVOICE_RECEIVED_DATE.</summary>
    public DateTime? InvoiceReceivedDateUtc { get; set; }

    /// <summary>Date the service or delivery was formally accepted. Reserved for future reference event support.</summary>
    public DateTime? ServiceAcceptanceDateUtc { get; set; }

    /// <summary>External invoice or billing reference number for traceability.</summary>
    public string? BillingReference { get; set; }

    /// <summary>Internal notes specific to this obligation instance.</summary>
    public string? ObligationNotes { get; set; }

    /// <summary>PENDING, REQUEST_CREATED, PAID, CANCELLED</summary>
    public string StatusCode { get; set; } = "PENDING";

    // Audit
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

