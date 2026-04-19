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

    public DateTime DueDateUtc { get; set; }

    /// <summary>PENDING, REQUEST_CREATED, PAID, CANCELLED</summary>
    public string StatusCode { get; set; } = "PENDING";

    // Audit
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
