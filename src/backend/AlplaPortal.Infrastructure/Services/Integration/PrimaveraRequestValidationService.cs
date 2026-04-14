using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera request-line validation service — composition layer.
///
/// Validates Portal request inputs against Primavera master data by composing
/// the existing read-only Primavera services. No direct SQL access.
///
/// Reused services:
///   - IPrimaveraArticleService         → article existence check
///   - IPrimaveraSupplierService        → supplier existence check
///   - IPrimaveraArticleSupplierService → relationship existence check
///
/// Validation Status Model:
///
///   VALID   — article exists, supplier exists (if provided), relationship exists (if both present)
///   WARNING — article exists + no supplier provided (partial validation)
///           — article exists + supplier exists + NO relationship (unlinked pair)
///   INVALID — article not found / article code missing
///           — supplier code provided but supplier not found
///   ERROR   — technical failure prevented validation
///
/// Important distinctions:
///   - Business INVALID ≠ technical ERROR
///   - "supplier not provided" is WARNING (partial), not INVALID
///   - "supplier provided but not found" is INVALID
///   - "both exist but no relationship" is WARNING (might be a new supplier)
/// </summary>
public class PrimaveraRequestValidationService : IPrimaveraRequestValidationService
{
    private readonly IPrimaveraArticleService _articleService;
    private readonly IPrimaveraSupplierService _supplierService;
    private readonly IPrimaveraArticleSupplierService _articleSupplierService;
    private readonly ILogger<PrimaveraRequestValidationService> _logger;

    public PrimaveraRequestValidationService(
        IPrimaveraArticleService articleService,
        IPrimaveraSupplierService supplierService,
        IPrimaveraArticleSupplierService articleSupplierService,
        ILogger<PrimaveraRequestValidationService> logger)
    {
        _articleService = articleService;
        _supplierService = supplierService;
        _articleSupplierService = articleSupplierService;
        _logger = logger;
    }

    public async Task<PrimaveraRequestValidationResultDto> ValidateRequestLineAsync(
        PrimaveraRequestValidationInputDto input)
    {
        var result = new PrimaveraRequestValidationResultDto
        {
            Company = input.Company,
            ArticleCode = input.ArticleCode?.Trim(),
            SupplierCode = string.IsNullOrWhiteSpace(input.SupplierCode) ? null : input.SupplierCode.Trim()
        };

        // 1. Parse company
        if (!Enum.TryParse(input.Company?.Trim(), ignoreCase: true, out PrimaveraCompany company))
        {
            result.ValidationStatus = "INVALID";
            result.Messages.Add($"Invalid or missing company: '{input.Company}'. " +
                                $"Valid values: {string.Join(", ", Enum.GetNames<PrimaveraCompany>())}");
            return result;
        }

        result.Company = company.ToString();

        // 2. Article code is required
        if (string.IsNullOrWhiteSpace(input.ArticleCode))
        {
            result.ValidationStatus = "INVALID";
            result.Messages.Add("Article code is required for validation.");
            return result;
        }

        var articleCode = input.ArticleCode.Trim();
        var supplierCode = result.SupplierCode;
        var hasSupplier = !string.IsNullOrWhiteSpace(supplierCode);

        _logger.LogInformation(
            "Request validation started. Company: {Company}, Article: {Article}, Supplier: {Supplier}",
            company, articleCode, supplierCode ?? "(not provided)");

        try
        {
            // 3. Check article existence
            var article = await _articleService.GetArticleByCodeAsync(company, articleCode);
            result.ArticleExists = article != null;
            result.ArticleDescription = article?.Description;

            if (!result.ArticleExists)
            {
                result.ValidationStatus = "INVALID";
                result.Messages.Add($"Article '{articleCode}' not found in {company}.");

                // If supplier was also provided, still report that we couldn't validate the relationship
                if (hasSupplier)
                {
                    result.Messages.Add("Supplier validation skipped because article does not exist.");
                }

                _logger.LogInformation(
                    "Validation result: INVALID. Article not found. Article: {Article}, Company: {Company}",
                    articleCode, company);

                return result;
            }

            result.Messages.Add($"Article '{articleCode}' exists in {company}: {result.ArticleDescription}");

            // 4. If no supplier provided → article-only validation = WARNING (partial)
            if (!hasSupplier)
            {
                result.ValidationStatus = "WARNING";
                result.Messages.Add("No supplier code provided. Validation is partial (article only).");

                _logger.LogInformation(
                    "Validation result: WARNING (partial). Article exists, no supplier. Article: {Article}, Company: {Company}",
                    articleCode, company);

                return result;
            }

            // 5. Check supplier existence
            var supplier = await _supplierService.GetSupplierByCodeAsync(company, supplierCode!);
            result.SupplierExists = supplier != null;
            result.SupplierName = supplier?.Name;

            if (!result.SupplierExists)
            {
                result.ValidationStatus = "INVALID";
                result.Messages.Add($"Supplier '{supplierCode}' not found in {company}.");
                result.Messages.Add("Article-supplier relationship not checked because supplier does not exist.");

                _logger.LogInformation(
                    "Validation result: INVALID. Supplier not found. Supplier: {Supplier}, Company: {Company}",
                    supplierCode, company);

                return result;
            }

            result.Messages.Add($"Supplier '{supplierCode}' exists in {company}: {result.SupplierName}");

            // 6. Check article-supplier relationship
            result.RelationshipChecked = true;
            var relationshipResult = await _articleSupplierService.GetSuppliersForArticleAsync(company, articleCode);

            if (relationshipResult != null && relationshipResult.Suppliers.Any(s =>
                    string.Equals(s.SupplierCode?.Trim(), supplierCode!.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                result.RelationshipExists = true;
                result.ValidationStatus = "VALID";
                result.Messages.Add($"Supplier '{supplierCode}' is linked to article '{articleCode}' in {company}.");

                _logger.LogInformation(
                    "Validation result: VALID. Article: {Article}, Supplier: {Supplier}, Company: {Company}",
                    articleCode, supplierCode, company);
            }
            else
            {
                result.RelationshipExists = false;
                result.ValidationStatus = "WARNING";
                result.Messages.Add($"Supplier '{supplierCode}' is NOT linked to article '{articleCode}' in {company}. " +
                                    "This article-supplier pair has no established relationship in Primavera.");

                _logger.LogInformation(
                    "Validation result: WARNING. No relationship. Article: {Article}, Supplier: {Supplier}, Company: {Company}",
                    articleCode, supplierCode, company);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Technical error during request validation. Article: {Article}, Supplier: {Supplier}, Company: {Company}",
                articleCode, supplierCode ?? "(none)", company);

            result.ValidationStatus = "ERROR";
            result.Messages.Add($"A technical error occurred during validation: {ex.Message}");
            return result;
        }
    }

    public async Task<List<PrimaveraRequestValidationResultDto>> ValidateRequestLinesAsync(
        List<PrimaveraRequestValidationInputDto> inputs)
    {
        var results = new List<PrimaveraRequestValidationResultDto>();

        foreach (var input in inputs)
        {
            results.Add(await ValidateRequestLineAsync(input));
        }

        return results;
    }
}
