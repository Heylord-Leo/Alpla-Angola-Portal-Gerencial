namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Individual row within an export batch. Mirrors the Excel row structure
/// for full traceability from export file ↔ MonthlyChangeItem ↔ AttendanceSnapshot.
///
/// Immutable after creation — never modified once the export is generated.
/// </summary>
public class MCExportRow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Parent ───

    public Guid ExportBatchId { get; set; }
    public MCExportBatch? ExportBatch { get; set; }

    // ─── Source Traceability ───

    public Guid MonthlyChangeItemId { get; set; }
    public MCMonthlyChangeItem? MonthlyChangeItem { get; set; }

    // ─── Export Row Data (denormalized, matches Excel columns) ───

    public int RowOrder { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string PrimaveraCode { get; set; } = string.Empty;
    public decimal Hours { get; set; }

    /// <summary>Explicitly optional in V1 (Amendment §4).</summary>
    public string? CostCenter { get; set; }

    /// <summary>Whether this row originated from a manual HR override.</summary>
    public bool WasManualOverride { get; set; }
}
