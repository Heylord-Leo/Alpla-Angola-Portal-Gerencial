namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable field-level audit log for request data changes.
/// Records the old and new values for every field modification, providing
/// full traceability for requests already in the workflow.
/// </summary>
public class RequestFieldChangeHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    /// <summary>Technical field name (e.g. "Description", "Title").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>User-facing field label (e.g. "Descrição", "Título").</summary>
    public string FieldDisplayName { get; set; } = string.Empty;

    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>The request status code at the moment the change was made.</summary>
    public string StatusCodeAtChange { get; set; } = string.Empty;

    /// <summary>Optional reference to a specific line item when the change is item-level.</summary>
    public Guid? LineItemId { get; set; }
    public RequestLineItem? LineItem { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
