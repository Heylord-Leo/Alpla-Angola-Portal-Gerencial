namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Represents a single article-supplier relationship row from Primavera.
///
/// Source: ArtigoFornecedor (25 columns; ~8 exposed here).
///
/// This DTO surfaces only the stable identity/context fields.
/// Pricing, discount, and transactional fields are intentionally excluded
/// from Phase 4C (relationship visibility only, not sourcing logic).
///
/// Deferred fields:
/// - PrCustoUltimo (last cost price)
/// - DescFor (supplier discount %)
/// - UltDescontoComercialCompra (last purchase discount)
/// - UltDespesaAdicionalCompra (last additional expense)
/// - PrecoUltEncomenda (last order price)
/// - IvaIncluido / CodIva / TaxaIva (VAT fields)
/// - TipoDoc / NumDoc / SerieDoc (last document reference)
/// - Classificacao (ranking/classification)
/// </summary>
public class PrimaveraArticleSupplierLinkDto
{
    /// <summary>Article code. Source: ArtigoFornecedor.Artigo</summary>
    public string ArticleCode { get; set; } = string.Empty;

    /// <summary>Supplier code. Source: ArtigoFornecedor.Fornecedor</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>Supplier's description of the article. Source: ArtigoFornecedor.DescricaoFor</summary>
    public string? SupplierDescription { get; set; }

    /// <summary>Supplier's reference for this article. Source: ArtigoFornecedor.ReferenciaFor</summary>
    public string? SupplierReference { get; set; }

    /// <summary>Purchase unit for this supplier. Source: ArtigoFornecedor.UnidadeCompra</summary>
    public string? PurchaseUnit { get; set; }

    /// <summary>Purchase conversion factor. Source: ArtigoFornecedor.FactorConvCompra</summary>
    public double? PurchaseConversionFactor { get; set; }

    /// <summary>Lead time in days. Source: ArtigoFornecedor.PrazoEntrega</summary>
    public short? LeadTimeDays { get; set; }

    /// <summary>Currency for this supplier-article pair. Source: ArtigoFornecedor.Moeda</summary>
    public string? Currency { get; set; }

    /// <summary>Last entry date. Source: ArtigoFornecedor.DatUltEntrada</summary>
    public DateTime? LastEntryDate { get; set; }
}

/// <summary>
/// Response DTO for "which suppliers supply this article?"
///
/// Wraps the article identity with its linked suppliers, enriched from
/// both ArtigoFornecedor and Fornecedores tables.
/// </summary>
public class PrimaveraArticleSuppliersDto
{
    /// <summary>The article code looked up.</summary>
    public string ArticleCode { get; set; } = string.Empty;

    /// <summary>The article description (from Artigo.Descricao).</summary>
    public string? ArticleDescription { get; set; }

    /// <summary>Number of linked suppliers found.</summary>
    public int SupplierCount { get; set; }

    /// <summary>Which Primavera company this data comes from.</summary>
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";

    /// <summary>The linked suppliers for this article.</summary>
    public List<ArticleSupplierItemDto> Suppliers { get; set; } = new();
}

/// <summary>
/// One supplier row in an article-to-suppliers response.
/// Combines ArtigoFornecedor relationship fields with Fornecedores identity.
/// </summary>
public class ArticleSupplierItemDto
{
    /// <summary>Supplier code.</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>Supplier name (from Fornecedores.Nome).</summary>
    public string? SupplierName { get; set; }

    /// <summary>Supplier's tax ID / NIF (from Fornecedores.NumContrib).</summary>
    public string? SupplierTaxId { get; set; }

    /// <summary>Supplier's description for this article.</summary>
    public string? SupplierDescription { get; set; }

    /// <summary>Supplier's reference code for this article.</summary>
    public string? SupplierReference { get; set; }

    /// <summary>Purchase unit for this supplier-article pair.</summary>
    public string? PurchaseUnit { get; set; }

    /// <summary>Lead time in days.</summary>
    public short? LeadTimeDays { get; set; }

    /// <summary>Currency.</summary>
    public string? Currency { get; set; }

    /// <summary>Last entry date.</summary>
    public DateTime? LastEntryDate { get; set; }
}

/// <summary>
/// Response DTO for "which articles does this supplier provide?"
///
/// Wraps the supplier identity with its linked articles, enriched from
/// both ArtigoFornecedor and Artigo tables.
/// </summary>
public class PrimaveraSupplierArticlesDto
{
    /// <summary>The supplier code looked up.</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>The supplier name (from Fornecedores.Nome).</summary>
    public string? SupplierName { get; set; }

    /// <summary>Number of linked articles found.</summary>
    public int ArticleCount { get; set; }

    /// <summary>Which Primavera company this data comes from.</summary>
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";

    /// <summary>The linked articles for this supplier.</summary>
    public List<SupplierArticleItemDto> Articles { get; set; } = new();
}

/// <summary>
/// One article row in a supplier-to-articles response.
/// Combines ArtigoFornecedor relationship fields with Artigo identity.
/// </summary>
public class SupplierArticleItemDto
{
    /// <summary>Article code.</summary>
    public string ArticleCode { get; set; } = string.Empty;

    /// <summary>Article description (from Artigo.Descricao).</summary>
    public string? ArticleDescription { get; set; }

    /// <summary>Base unit (from Artigo.UnidadeBase).</summary>
    public string? BaseUnit { get; set; }

    /// <summary>Supplier's description for this article.</summary>
    public string? SupplierDescription { get; set; }

    /// <summary>Supplier's reference code for this article.</summary>
    public string? SupplierReference { get; set; }

    /// <summary>Purchase unit for this supplier-article pair.</summary>
    public string? PurchaseUnit { get; set; }

    /// <summary>Lead time in days.</summary>
    public short? LeadTimeDays { get; set; }

    /// <summary>Currency.</summary>
    public string? Currency { get; set; }

    /// <summary>Last entry date.</summary>
    public DateTime? LastEntryDate { get; set; }
}
