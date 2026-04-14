using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Primavera article-supplier relationship read service contract.
///
/// All operations are read-only and company-aware.
/// This interface is Primavera-specific and not part of the generic
/// integration provider contract.
///
/// Phase 4C scope:
/// - discover suppliers for an article
/// - discover articles for a supplier
/// - no pricing/ranking/sourcing logic
/// - no sync or writeback
/// </summary>
public interface IPrimaveraArticleSupplierService
{
    /// <summary>
    /// Gets the suppliers linked to a given article code.
    /// Returns null if the article itself does not exist.
    /// Returns an empty Suppliers list if the article exists but has no linked suppliers.
    /// </summary>
    Task<PrimaveraArticleSuppliersDto?> GetSuppliersForArticleAsync(PrimaveraCompany company, string articleCode);

    /// <summary>
    /// Gets the articles linked to a given supplier code.
    /// Returns null if the supplier itself does not exist.
    /// Returns an empty Articles list if the supplier exists but has no linked articles.
    /// </summary>
    Task<PrimaveraSupplierArticlesDto?> GetArticlesForSupplierAsync(PrimaveraCompany company, string supplierCode);
}
