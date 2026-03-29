using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Persisted admin-queryable operational event. Written explicitly by services
/// via AdminLogWriter — not a generic logger sink.
/// </summary>
public class AdminLogEntry
{
    public int Id { get; set; }

    [Required]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Information | Warning | Error</summary>
    [Required, MaxLength(20)]
    public string Level { get; set; } = "Information";

    /// <summary>Component or service that produced the event.</summary>
    [Required, MaxLength(256)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Machine-readable event code, e.g. OCR_SETTINGS_SAVED,
    /// OCR_SETTINGS_VALIDATION_FAILED, OCR_PROVIDER_TEST_OK.
    /// </summary>
    [Required, MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Human-readable summary message.</summary>
    [Required]
    public string Message { get; set; } = string.Empty;

    /// <summary>Correlation/request ID — set by CorrelationIdMiddleware.</summary>
    [MaxLength(50)]
    public string? CorrelationId { get; set; }

    /// <summary>User email from server-side request context. Never from client payload.</summary>
    [MaxLength(256)]
    public string? UserEmail { get; set; }

    /// <summary>
    /// Formatted exception type + message + stack trace.
    /// Only populated for real exceptions (Level = Error).
    /// Null for validation failures and informational events.
    /// </summary>
    public string? ExceptionDetail { get; set; }

    /// <summary>Safe shaped payload (never raw request bodies). Sanitized before persistence.</summary>
    public string? Payload { get; set; }
}
