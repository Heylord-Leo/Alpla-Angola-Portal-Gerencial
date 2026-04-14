using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Primavera request-line validation service contract.
///
/// This is a composition/validation layer built above the existing
/// Primavera master-data services:
///   - IPrimaveraArticleService   (article existence)
///   - IPrimaveraSupplierService  (supplier existence)
///   - IPrimaveraArticleSupplierService (relationship existence)
///
/// All operations are read-only and company-aware.
/// No writes, sync, or Primavera mutations.
///
/// Phase 5A scope:
///   - single-line validation: article + optional supplier
///   - batch validation: multiple lines in one call
///   - deterministic code-based rules, no fuzzy matching
///   - no sourcing/ranking/pricing logic
/// </summary>
public interface IPrimaveraRequestValidationService
{
    /// <summary>
    /// Validates a single request line context against Primavera master data.
    ///
    /// Checks article existence, supplier existence (if provided),
    /// and article-supplier relationship (if both exist).
    ///
    /// Returns a structured result that separates technical errors
    /// from business-level validation outcomes.
    /// </summary>
    Task<PrimaveraRequestValidationResultDto> ValidateRequestLineAsync(
        PrimaveraRequestValidationInputDto input);

    /// <summary>
    /// Validates multiple request lines in a single call.
    /// Each line is validated independently against the same validation rules.
    /// </summary>
    Task<List<PrimaveraRequestValidationResultDto>> ValidateRequestLinesAsync(
        List<PrimaveraRequestValidationInputDto> inputs);
}
