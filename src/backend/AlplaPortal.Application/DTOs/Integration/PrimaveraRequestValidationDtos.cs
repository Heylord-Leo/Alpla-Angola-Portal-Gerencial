namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Result of validating a single request line context against Primavera master data.
///
/// This is the output of the composition/validation layer, NOT a data-access object.
/// It reports whether the referenced article, supplier, and their relationship
/// exist in the specified Primavera company.
///
/// Validation Status Model (Phase 5A):
///
///   VALID   — all provided entities exist and any applicable relationships hold.
///   WARNING — entities exist but relationships are missing or supplier was omitted.
///   INVALID — one or more required entities (article/supplier) do not exist.
///   ERROR   — a technical failure prevented validation from completing.
///
/// Technical failures (SQL timeout, provider misconfiguration) are surfaced
/// as ERROR, distinct from business-level INVALID. Callers must distinguish
/// "user provided bad input" from "system could not validate".
/// </summary>
public class PrimaveraRequestValidationResultDto
{
    // ── Input echo ────────────────────────────────────────────────
    /// <summary>Which Primavera company was validated against.</summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>The article code that was validated (null if not provided).</summary>
    public string? ArticleCode { get; set; }

    /// <summary>The supplier code that was validated (null if not provided).</summary>
    public string? SupplierCode { get; set; }

    // ── Existence checks ──────────────────────────────────────────
    /// <summary>Whether the article was found in the target company.</summary>
    public bool ArticleExists { get; set; }

    /// <summary>Article description from Primavera (if found).</summary>
    public string? ArticleDescription { get; set; }

    /// <summary>Whether the supplier was found in the target company.</summary>
    public bool SupplierExists { get; set; }

    /// <summary>Supplier name from Primavera (if found).</summary>
    public string? SupplierName { get; set; }

    // ── Relationship check ────────────────────────────────────────
    /// <summary>Whether the article-supplier relationship check was performed.</summary>
    public bool RelationshipChecked { get; set; }

    /// <summary>Whether the supplier is linked to the article in this company.</summary>
    public bool RelationshipExists { get; set; }

    // ── Overall result ────────────────────────────────────────────
    /// <summary>
    /// Overall validation status: VALID, WARNING, INVALID, or ERROR.
    ///
    /// Decision rules (Phase 5A):
    ///   VALID   — article exists + (supplier exists + relationship exists) OR (no supplier provided)
    ///   WARNING — article exists + supplier provided but no relationship found
    ///           — article exists + no supplier provided (partial validation)
    ///   INVALID — article not found, or supplier provided but not found
    ///   ERROR   — technical failure during validation
    /// </summary>
    public string ValidationStatus { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable messages describing the validation outcome.
    /// Includes both errors and informational context.
    /// </summary>
    public List<string> Messages { get; set; } = new();

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";
}

/// <summary>
/// Input payload for a single request-line validation against Primavera master data.
/// </summary>
public class PrimaveraRequestValidationInputDto
{
    /// <summary>
    /// Required. The Primavera company to validate against.
    /// Must be a valid PrimaveraCompany name (e.g. "ALPLAPLASTICO", "ALPLASOPRO").
    /// </summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// Required. The article code to validate.
    /// </summary>
    public string? ArticleCode { get; set; }

    /// <summary>
    /// Optional. The supplier code to validate.
    /// If provided, supplier existence and article-supplier relationship are also checked.
    /// If omitted, only article existence is validated (result may be WARNING).
    /// </summary>
    public string? SupplierCode { get; set; }
}
