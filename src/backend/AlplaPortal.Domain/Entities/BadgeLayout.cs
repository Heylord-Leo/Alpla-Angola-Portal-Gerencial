namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Badge Layout — template definition for employee badge rendering.
///
/// Versioning workflow (immutable-active pattern):
/// - New layouts start as DRAFT.
/// - Only DRAFT layouts can be edited in place.
/// - Publishing a DRAFT sets it to ACTIVE and archives the previous ACTIVE
///   for the same activation scope (Company + BadgeType).
/// - Editing an ACTIVE layout creates a new DRAFT version (Version + 1).
/// - ARCHIVED layouts are read-only historical records.
///
/// Activation scope determines which layout is used for a given context:
/// - CompanyCode: null = all companies, "ALPLAPLASTICO" = specific
/// - BadgeType: null = default, "VISITOR" = visitor badge, etc.
/// - PlantCode: reserved for future per-plant overrides (null for now)
///
/// The layout configuration itself is stored as a JSON blob in LayoutConfigJson,
/// allowing flexible schema evolution without database migrations.
/// </summary>
public class BadgeLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-facing template name, e.g. "Crachá Padrão Plástico".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of this layout template.</summary>
    public string? Description { get; set; }

    /// <summary>Incremental version number. New edits on active layouts create Version + 1 as draft.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Lifecycle status: DRAFT, ACTIVE, ARCHIVED.</summary>
    public string Status { get; set; } = BadgeLayoutStatus.Draft;

    // ─── Layout Configuration (JSON blob) ───

    /// <summary>
    /// JSON-serialized layout configuration containing element positions, colors,
    /// visibility toggles, and styling. Schema is versioned within the JSON itself.
    /// </summary>
    public string LayoutConfigJson { get; set; } = "{}";

    // ─── Activation Scope ───

    /// <summary>Company filter. null = applicable to all companies.</summary>
    public string? CompanyCode { get; set; }

    /// <summary>Badge type. null = default employee badge. "VISITOR", "CONTRACTOR", etc.</summary>
    public string? BadgeType { get; set; }

    /// <summary>Plant filter (reserved for future use). null = all plants.</summary>
    public string? PlantCode { get; set; }

    // ─── Audit ───

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>Badge layout lifecycle status constants.</summary>
public static class BadgeLayoutStatus
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Archived = "ARCHIVED";
}
