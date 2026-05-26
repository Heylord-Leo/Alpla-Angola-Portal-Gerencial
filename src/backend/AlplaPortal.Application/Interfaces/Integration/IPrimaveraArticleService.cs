using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Primavera article/material master-data read service — company-aware.
///
/// This is a Primavera-specific domain service layered above the
/// generic provider foundation. It does not belong in the provider contract.
///
/// Phase 4A scope: lookup by code + search by query. Read-only.
/// No stock, pricing, supplier-relationship, or sync logic.
/// </summary>
public interface IPrimaveraArticleService
{
    /// <summary>
    /// Looks up a single article by its Primavera code in the specified company.
    /// Returns null if not found.
    /// </summary>
    Task<PrimaveraArticleDto?> GetArticleByCodeAsync(PrimaveraCompany company, string code);

    /// <summary>
    /// Searches articles by code or description fragment in the specified company.
    /// Results are bounded by <paramref name="limit"/> (max 50).
    /// Minimum query length: 2 characters.
    /// </summary>
    Task<List<PrimaveraArticleDto>> SearchArticlesAsync(
        PrimaveraCompany company, string query, int limit = 50);

    /// <summary>
    /// Lists articles from Primavera with optional search filtering and pagination.
    /// Used by the synchronization preview to fetch large datasets.
    /// When search is null/empty, returns all non-cancelled articles.
    /// </summary>
    Task<(List<PrimaveraArticleDto> Items, int TotalCount)> ListArticlesAsync(
        PrimaveraCompany company, string? search = null, int page = 1, int pageSize = 50);

    /// <summary>
    /// Lists ALL articles from Primavera with optional search filtering (no pagination).
    /// Used when post-match filtering (e.g., by status) requires full dataset access
    /// to produce correctly paginated results.
    /// </summary>
    Task<List<PrimaveraArticleDto>> ListAllArticlesAsync(
        PrimaveraCompany company, string? search = null);
}
