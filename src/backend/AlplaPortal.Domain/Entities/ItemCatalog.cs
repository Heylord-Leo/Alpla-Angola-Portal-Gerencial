namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Master data catalog for structured items that requesters can select
/// when creating Payment or Quotation requests.
/// 
/// This entity is the Portal's operational source of truth for item definitions.
/// Initial data may be imported from SharePoint (one-time seed), after which
/// all maintenance happens within the Portal.
///
/// Designed for soft lifecycle (active/inactive) — no hard deletes.
/// The Origin field provides traceability for how each record was created.
/// </summary>
public class ItemCatalog
{
    public int Id { get; set; }

    /// <summary>Unique item reference code (e.g., from Primavera / SharePoint).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable item description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional internal Primavera ERP code.</summary>
    public string? PrimaveraCode { get; set; }

    /// <summary>Optional primary supplier code.</summary>
    public string? SupplierCode { get; set; }

    /// <summary>Optional default unit of measure for this item.</summary>
    public int? DefaultUnitId { get; set; }
    public Unit? DefaultUnit { get; set; }

    /// <summary>Optional category/family grouping for filtering.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Record origin for operational traceability.
    /// Values: "MANUAL", "IMPORTED_SHAREPOINT", "SYNCED_PRIMAVERA"
    /// </summary>
    public string Origin { get; set; } = "MANUAL";

    /// <summary>
    /// Source Primavera company/database this record was synchronized from.
    /// Null for manually created or SharePoint-imported records.
    /// Values: "ALPLAPLASTICO", "ALPLASOPRO"
    /// </summary>
    public string? SourceCompany { get; set; }

    /// <summary>
    /// Timestamp of the last synchronization that touched this record.
    /// Null for manually created records.
    /// </summary>
    public DateTime? LastSyncedAtUtc { get; set; }

    /// <summary>Soft lifecycle flag — inactive items are hidden from selection but preserved for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

