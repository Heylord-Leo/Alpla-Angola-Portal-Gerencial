using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Primavera supplier master-data read service contract.
///
/// All operations are read-only and company-aware.
/// This interface is Primavera-specific and not part of the generic
/// integration provider contract.
///
/// Phase 4B scope:
/// - lookup by supplier code
/// - search by name or code fragment
/// - no stock, pricing, banking, or transactional queries
/// - no sync or writeback
/// </summary>
public interface IPrimaveraSupplierService
{
    /// <summary>
    /// Looks up a single supplier by its Primavera supplier code.
    /// Returns null if not found.
    /// </summary>
    Task<PrimaveraSupplierDto?> GetSupplierByCodeAsync(PrimaveraCompany company, string code);

    /// <summary>
    /// Searches for suppliers by name or code fragment.
    /// Results are bounded by <paramref name="limit"/> (max 50).
    /// Returns empty list if query is too short or no matches found.
    /// </summary>
    Task<List<PrimaveraSupplierDto>> SearchSuppliersAsync(PrimaveraCompany company, string query, int limit = 50);

    /// <summary>
    /// Lists suppliers from Primavera with optional search filtering and pagination.
    /// Used by the synchronization preview to fetch large datasets.
    /// When search is null/empty, returns all non-cancelled suppliers.
    /// </summary>
    Task<(List<PrimaveraSupplierDto> Items, int TotalCount)> ListSuppliersAsync(
        PrimaveraCompany company, string? search = null, int page = 1, int pageSize = 50);

    /// <summary>
    /// Lists ALL suppliers from Primavera with optional search filtering (no pagination).
    /// Used when post-match filtering (e.g., by status) requires full dataset access
    /// to produce correctly paginated results.
    /// </summary>
    Task<List<PrimaveraSupplierDto>> ListAllSuppliersAsync(
        PrimaveraCompany company, string? search = null);
}
