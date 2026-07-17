namespace AlplaPortal.Application.Interfaces;

/// <summary>Lightweight supplier projection returned to callers (existing match or created supplier).</summary>
public sealed class SupplierSummaryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? TaxId { get; init; }
    public string? PortalCode { get; init; }
    public string? PrimaveraCode { get; init; }
    public bool IsActive { get; init; }
    public string RegistrationStatus { get; init; } = string.Empty;
}

public enum SupplierCreationStatus
{
    /// <summary>A new DRAFT supplier was created.</summary>
    Created,
    /// <summary>A supplier with the same NIF (or same name+NIF) already exists — creation blocked.</summary>
    Conflict,
    /// <summary>A supplier with the same normalized name but different/absent NIF exists — needs confirmation.</summary>
    DuplicateSuspected,
    /// <summary>Input is invalid (e.g. empty name).</summary>
    Invalid,
    /// <summary>The NIF belongs to an internal ALPLA company — must never be registered as a supplier.</summary>
    InternalCompanyTaxId,
    /// <summary>Unexpected persistence error.</summary>
    Error
}

public sealed class SupplierCreationResult
{
    public SupplierCreationStatus Status { get; init; }
    /// <summary>The created supplier (Created) or the existing one (Conflict).</summary>
    public SupplierSummaryDto? Supplier { get; init; }
    /// <summary>Candidates for a suspected duplicate.</summary>
    public List<SupplierSummaryDto> Candidates { get; init; } = new();
    /// <summary>Stable machine code, e.g. SUPPLIER_ALREADY_EXISTS, SUPPLIER_INACTIVE_EXISTS, SUPPLIER_DUPLICATE_SUSPECTED, INTERNAL_COMPANY_TAX_ID.</summary>
    public string? Code { get; init; }
    public string? Message { get; init; }

    /// <summary>Set when the NIF belongs to an internal company (Status = InternalCompanyTaxId).</summary>
    public int? InternalCompanyId { get; init; }
    public string? InternalCompanyName { get; init; }
    public string? InternalCompanyTaxId { get; init; }
}

/// <summary>Data allowed for a DRAFT supplier creation. Administrative fields are intentionally excluded.</summary>
public sealed class SupplierCreationInput
{
    public string Name { get; init; } = string.Empty;
    public string? TaxId { get; init; }
    public string? Address { get; init; }
    public string? ContactName { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }

    /// <summary>Only populated by the general admin endpoint. The contextual payment-OCR flow leaves this null.</summary>
    public string? PrimaveraCode { get; init; }

    /// <summary>Provenance for <see cref="Domain.Entities.Supplier.Origin"/>: "MANUAL" or "PAYMENT_OCR".</summary>
    public string Origin { get; init; } = "MANUAL";

    /// <summary>When true, proceed despite a suspected (same-name/different-NIF) duplicate.</summary>
    public bool ConfirmCreateDespiteDuplicate { get; init; }

    // ── Audit (structured provenance recorded in SupplierStatusHistory) ──
    public string? ExtractedName { get; init; }
    public string? ExtractedTaxId { get; init; }

    /// <summary>When the OCR-extracted NIF belonged to an internal company and was dropped, its value —
    /// recorded for audit (the supplier is created without a NIF).</summary>
    public string? InternalCompanyTaxIdExtracted { get; init; }

    /// <summary>When the user was shown an existing supplier matched by name and explicitly declined it
    /// before creating a new no-NIF supplier, that supplier's id — recorded for audit.</summary>
    public int? RejectedSuggestedSupplierId { get; init; }
}

/// <summary>
/// Single source of truth for supplier matching + DRAFT creation, shared by the general admin endpoint
/// and the contextual payment-OCR endpoint. Owns normalization (name/NIF), authoritative matching,
/// duplicate/inactive handling, PortalCode generation (concurrency-safe), unique-index retry and audit.
/// Controllers own only authorization, HTTP contract and result translation.
/// </summary>
public interface ISupplierCreationService
{
    /// <summary>Authoritative match (no writes) — used by the frontend to decide whether to offer creation.</summary>
    Task<SupplierCreationResult> MatchAsync(string? name, string? taxId);

    /// <summary>Match then create a DRAFT supplier when allowed. Concurrency/uniqueness handled internally.</summary>
    Task<SupplierCreationResult> CreateAsync(SupplierCreationInput input, Guid actorId);
}
