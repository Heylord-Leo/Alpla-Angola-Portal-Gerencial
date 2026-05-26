namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Primavera article/material master-data DTO — read-only.
///
/// Source: Primavera table [Artigo] with LEFT JOIN [Familias].
///
/// Design rules:
/// - Focused on stable item-master identity fields only
/// - No stock balances, pricing, purchase history, or supplier relationships
/// - SubFamilias enrichment intentionally deferred
/// - Remarks (Observacoes) intentionally deferred
/// - Source traceability always included
///
/// Environment-specific custom fields:
/// - SupplierCode (CDU_codforneced) — custom column, not standard Primavera
/// - InternalCode (CDU_codinterno) — custom column, not standard Primavera
/// These fields exist in the Alpla Angola Primavera environment but are NOT
/// guaranteed to exist in every Primavera installation.
/// </summary>
public class PrimaveraArticleDto
{
    // ─── Core identity ───

    /// <summary>Primavera article code (Artigo). Primary key.</summary>
    public required string Code { get; set; }

    /// <summary>Full article description (Descricao).</summary>
    public string? Description { get; set; }

    // ─── Classification ───

    /// <summary>Base unit of measure (UnidadeBase).</summary>
    public string? BaseUnit { get; set; }

    /// <summary>Purchase unit of measure (UnidadeCompra).</summary>
    public string? PurchaseUnit { get; set; }

    /// <summary>Family code (Familia).</summary>
    public string? Family { get; set; }

    /// <summary>Family description from [Familias] join.</summary>
    public string? FamilyDescription { get; set; }

    /// <summary>Sub-family code (SubFamilia). Enrichment join deferred.</summary>
    public string? SubFamily { get; set; }

    /// <summary>Primavera article type code (TipoArtigo).</summary>
    public int? ArticleType { get; set; }

    /// <summary>Brand (Marca).</summary>
    public string? Brand { get; set; }

    // ─── Status ───

    /// <summary>Whether the article is voided/cancelled (ArtigoAnulado).</summary>
    public bool IsCancelled { get; set; }

    // ─── Environment-specific custom fields ───
    // These are custom columns (CDU_*) specific to the Alpla Angola environment.
    // They are NOT standard Primavera fields and may not exist in other installations.

    /// <summary>
    /// Supplier-side article code (CDU_codforneced).
    /// Environment-specific custom field — not standard Primavera.
    /// </summary>
    public string? SupplierCode { get; set; }

    /// <summary>
    /// Alpla internal article code (CDU_codinterno).
    /// Environment-specific custom field — not standard Primavera.
    /// </summary>
    public string? InternalCode { get; set; }

    // ─── Traceability ───

    /// <summary>Primavera company/database this record came from.</summary>
    public string? SourceCompany { get; set; }

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";
}
