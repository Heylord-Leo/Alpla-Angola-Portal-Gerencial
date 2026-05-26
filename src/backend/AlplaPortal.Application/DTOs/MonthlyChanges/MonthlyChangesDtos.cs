namespace AlplaPortal.Application.DTOs.MonthlyChanges;

// ─── Request DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// Request to create a new processing run for a specific entity and month.
/// </summary>
public class CreateProcessingRunRequest
{
    /// <summary>Innux IDEntidade (1 = AlplaPLASTICO, 6 = AlplaSOPRO).</summary>
    public int EntityId { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
}

// ─── Response DTOs ───────────────────────────────────────────────────────────

/// <summary>
/// Summary view of a processing run — used in list and dashboard views.
/// </summary>
public class ProcessingRunSummaryDto
{
    public Guid Id { get; set; }
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public int SyncedRowCount { get; set; }
    public int OccurrenceCount { get; set; }
    public int AnomalyCount { get; set; }
    public int UnresolvedCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByEmail { get; set; }
    public DateTime? SyncedAtUtc { get; set; }
    public DateTime? DetectionCompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Detail view of a processing run — includes statistics and child counts.
/// </summary>
public class ProcessingRunDetailDto : ProcessingRunSummaryDto
{
    public int ApprovedCount { get; set; }
    public int AdjustedCount { get; set; }
    public int ExcludedCount { get; set; }
    public int AutoCodedCount { get; set; }
    public int NeedsReviewCount { get; set; }
}

/// <summary>
/// Monthly change item — one detected occurrence in the review list.
/// </summary>
public class MonthlyChangeItemDto
{
    public Guid Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string OccurrenceType { get; set; } = string.Empty;
    public string DetectionRule { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? ScheduleCode { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string? PrimaveraCode { get; set; }
    public string? PrimaveraCodeDescription { get; set; }
    public decimal? Hours { get; set; }
    public string? CostCenter { get; set; }
    public bool IsManualOverride { get; set; }
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Anomaly-specific view — filtered subset of monthly change items with anomaly context.
/// </summary>
public class AnomalyItemDto
{
    public Guid Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string OccurrenceType { get; set; } = string.Empty;
    public string? AnomalyReason { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string? ResolutionNote { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

/// <summary>
/// Processing log entry for pipeline diagnostics.
/// </summary>
public class ProcessingLogDto
{
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

// ─── Label Helpers ───────────────────────────────────────────────────────────

/// <summary>
/// Portuguese label mappings for run and item status codes (Amendment §2).
/// Keeps UI wording distinct between run-level and item-level states.
/// </summary>
public static class MCStatusLabels
{
    public static string RunLabel(string statusCode) => statusCode switch
    {
        "DRAFT" => "Rascunho",
        "SYNCING" => "A Processar...",
        "NEEDS_REVIEW" => "Em Revisão",
        "READY_FOR_EXPORT" => "Pronto para Exportar",
        "EXPORTED" => "Exportado",
        "CLOSED" => "Encerrado",
        "FAILED" => "Erro",
        _ => statusCode
    };

    public static string ItemLabel(string statusCode) => statusCode switch
    {
        "AUTO_CODED" => "Sugestão Automática",
        "NEEDS_REVIEW" => "Pendente",
        "APPROVED" => "Aprovado",
        "ADJUSTED" => "Ajustado",
        "EXCLUDED" => "Excluído",
        "EXPORTED" => "Exportado",
        _ => statusCode
    };

    // ─── Entity name mapping ─────────────────────────────────────────────

    public static string EntityName(int entityId) => entityId switch
    {
        1 => "AlplaPLASTICO",
        6 => "AlplaSOPRO",
        _ => $"Entity {entityId}"
    };
}
