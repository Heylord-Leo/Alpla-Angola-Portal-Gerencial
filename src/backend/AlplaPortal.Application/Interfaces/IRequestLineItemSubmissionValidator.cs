namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// A single line-item candidate to validate. Unit is expressed as an already-resolved UnitId
/// (the caller resolves quotation unit codes → ids and payment items already carry UnitId), so the
/// validator stays free of persistence and HTTP concerns.
/// </summary>
public sealed class LineItemCandidate
{
    /// <summary>Stable index for error reporting (DTO position for create, LineNumber for submit).</summary>
    public int Index { get; init; }
    public string? Description { get; init; }
    public decimal Quantity { get; init; }
    public int? UnitId { get; init; }
    /// <summary>Derived line total (authoritative TotalAmount). Only meaningful for payment submit.</summary>
    public decimal LineTotal { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed class LineItemValidationError
{
    /// <summary>Item index the error refers to, or null for a request-level error (e.g. "no items").</summary>
    public int? ItemIndex { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class LineItemValidationResult
{
    public List<LineItemValidationError> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
    /// <summary>Distinct messages joined — matches the current API's single-string Detail style.</summary>
    public string Summary => string.Join(" ", Errors.Select(e => e.Message).Distinct());

    public LineItemValidationResult Add(string message, int? itemIndex = null, string field = "")
    {
        Errors.Add(new LineItemValidationError { ItemIndex = itemIndex, Field = field, Message = message });
        return this;
    }
}

/// <summary>
/// Pure, reusable validation of line-item validity for the two Phase-2 gates:
/// QUOTATION at CreateRequest and PAYMENT at Submit. Knows nothing about HTTP, EF or authorization.
/// The controller loads data (including the set of valid — active — unit ids), calls the validator,
/// and maps the structured result to the API response.
/// </summary>
public interface IRequestLineItemSubmissionValidator
{
    /// <summary>
    /// QUOTATION create gate: valid when there is at least one item AND EVERY item is valid
    /// (non-empty description, quantity &gt; 0, unit present and in <paramref name="validUnitIds"/>).
    /// A null/empty list is rejected, and a single invalid row rejects the whole set.
    /// </summary>
    LineItemValidationResult ValidateQuotation(IReadOnlyList<LineItemCandidate>? items, ISet<int> validUnitIds);

    /// <summary>
    /// PAYMENT submit gate: valid when there is at least one active item AND EVERY active item is valid
    /// (non-empty description, quantity &gt; 0, unit present and in <paramref name="validUnitIds"/>,
    /// line total &gt; 0). A zero/invalid line is never masked by another valid line.
    /// </summary>
    LineItemValidationResult ValidatePaymentSubmit(IReadOnlyList<LineItemCandidate>? items, ISet<int> validUnitIds);
}
