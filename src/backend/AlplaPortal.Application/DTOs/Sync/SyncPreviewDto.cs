using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Sync;

/// <summary>
/// Classification for each record during a Primavera → Portal synchronization preview.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SyncMatchStatus
{
    /// <summary>Record exists in Primavera but has no match in the Portal — available for import.</summary>
    New,

    /// <summary>Record already exists in the Portal (matched by primary key) — no action needed.</summary>
    Exists,

    /// <summary>
    /// Suspicious match detected (e.g., same TaxId but different code, or similar name).
    /// Requires manual review — not importable in V1.
    /// </summary>
    Conflict
}

// ──────────────────────────────────────────────────────────────────────────────
// Item Catalog Sync
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Single item comparison row in the Catalog sync preview.
/// </summary>
public class CatalogSyncPreviewItemDto
{
    // ── Primavera side ──
    public string PrimaveraCode { get; set; } = string.Empty;
    public string? PrimaveraDescription { get; set; }
    public string? PrimaveraFamily { get; set; }
    public string? PrimaveraBaseUnit { get; set; }
    public bool PrimaveraIsCancelled { get; set; }

    // ── Portal side (populated if matched) ──
    public int? PortalItemId { get; set; }
    public string? PortalCode { get; set; }
    public string? PortalDescription { get; set; }

    // ── Matching ──
    public SyncMatchStatus Status { get; set; }
    public string? ConflictDetail { get; set; }
}

/// <summary>
/// Full preview result for Item Catalog synchronization.
/// </summary>
public class CatalogSyncPreviewDto
{
    public int TotalPrimaveraRecords { get; set; }
    public int NewCount { get; set; }
    public int ExistsCount { get; set; }
    public int ConflictCount { get; set; }
    public List<CatalogSyncPreviewItemDto> Items { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Supplier Sync
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Single supplier comparison row in the Supplier sync preview.
/// </summary>
public class SupplierSyncPreviewItemDto
{
    // ── Primavera side ──
    public string PrimaveraCode { get; set; } = string.Empty;
    public string? PrimaveraName { get; set; }
    public string? PrimaveraTaxId { get; set; }
    public bool PrimaveraIsCancelled { get; set; }

    // ── Portal side (populated if matched) ──
    public int? PortalSupplierId { get; set; }
    public string? PortalName { get; set; }
    public string? PortalPrimaveraCode { get; set; }
    public string? PortalTaxId { get; set; }

    // ── Matching ──
    public SyncMatchStatus Status { get; set; }
    public string? ConflictDetail { get; set; }
}

/// <summary>
/// Full preview result for Supplier synchronization.
/// </summary>
public class SupplierSyncPreviewDto
{
    public int TotalPrimaveraRecords { get; set; }
    public int NewCount { get; set; }
    public int ExistsCount { get; set; }
    public int ConflictCount { get; set; }
    public List<SupplierSyncPreviewItemDto> Items { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Import contracts (shared for both entities)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for import: the user's selection of Primavera codes to import.
/// </summary>
public class SyncImportRequestDto
{
    public List<string> SelectedPrimaveraCodes { get; set; } = new();
}

/// <summary>
/// Result of a sync import operation.
/// </summary>
public class SyncImportResultDto
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Reviewed Supplier Import (V2 — user-edited data before import)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single supplier row reviewed and potentially edited by the user
/// in the import review modal before submission.
/// </summary>
public class ReviewedSupplierItemDto
{
    /// <summary>Primavera code — read-only identifier, not editable by the user.</summary>
    public string PrimaveraCode { get; set; } = string.Empty;

    /// <summary>Supplier name — editable by the user during review. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>NIF / Tax ID — editable by the user during review. Optional.</summary>
    public string? TaxId { get; set; }

    /// <summary>Free-text notes added by the user during review. Optional.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Request body for the reviewed supplier import endpoint.
/// Contains the list of suppliers with user-validated/edited data.
/// </summary>
public class SyncSupplierReviewedImportRequestDto
{
    public List<ReviewedSupplierItemDto> Suppliers { get; set; } = new();
}
