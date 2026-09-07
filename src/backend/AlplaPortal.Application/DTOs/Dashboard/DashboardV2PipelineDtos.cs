using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

// ── B6: Dashboard V2 canonical Operational Pipeline (GERENCIAL, read-only). ──
// Replaces the legacy scalar Request.Status histogram. Each stage is measured on its own canonical
// entity unit and a request MAY contribute to several stages at once (CanOverlap) — a request with a
// group in PO, another scheduled for payment and a third in receiving appears in three stages. The
// headline UniqueActiveRequests is the distinct active-request denominator; it is NOT the sum of stage
// counts. No aging, no money, no urgency/overdue, no alerts (those are B7/B8/B9). Help/tooltip copy is
// owned by the frontend, never carried in this DTO.

public static class PipelineDomains
{
    public const string Preparacao = "PREPARACAO";
    public const string Compras = "COMPRAS";
    public const string Aprovacoes = "APROVACOES";
    public const string Po = "PO";
    public const string Financas = "FINANCAS";
    public const string Recebimento = "RECEBIMENTO";
    public const string Documentacao = "DOCUMENTACAO";
    public const string Conclusao = "CONCLUSAO";
}

public static class PipelineEntityTypes
{
    public const string Request = "REQUEST";
    public const string LineItem = "LINE_ITEM";
    public const string ApprovalBatch = "APPROVAL_BATCH";
    public const string PoGroup = "PO_GROUP";
}

public static class PipelineStages
{
    public const string Draft = "DRAFT";
    public const string NeedsQuotation = "NEEDS_QUOTATION";
    public const string PartialCoverage = "PARTIAL_COVERAGE";
    public const string ReadyForApproval = "READY_FOR_APPROVAL";
    public const string AreaApproval = "AREA_APPROVAL";
    public const string FinalApproval = "FINAL_APPROVAL";
    public const string Adjustment = "ADJUSTMENT";
    public const string PoWaiting = "PO_WAITING";
    public const string PoCorrection = "PO_CORRECTION";
    public const string FinanceNeedsScheduling = "FIN_NEEDS_SCHEDULING";
    public const string FinanceScheduled = "FIN_SCHEDULED";
    public const string FinancePaid = "FIN_PAID";
    public const string ReceivingReady = "REC_READY";
    public const string ReceivingWaiting = "REC_WAITING";
    public const string ReceivingFollowup = "REC_FOLLOWUP";
    public const string ReceivingSupplier = "REC_SUPPLIER";
    public const string Documentation = "DOCUMENTATION";
    public const string Completed = "COMPLETED";
}

/// <summary>
/// One operational pipeline stage. <see cref="EntityCount"/> is measured in <see cref="EntityType"/>
/// units (requests, approval batches, or PO groups); <see cref="RequestCount"/> is the distinct
/// underlying requests. <see cref="CanOverlap"/> is always true — stages are not mutually exclusive.
/// <see cref="TargetPath"/> is set ONLY where an exact canonical filter already exists (else null).
/// </summary>
public sealed class OperationalPipelineStageDto
{
    public string Domain { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public int RequestCount { get; set; }
    public int SortOrder { get; set; }
    public string? TargetPath { get; set; }
    public bool CanOverlap { get; set; } = true;
}

public sealed class DashboardV2PipelineDto
{
    /// <summary>Distinct active requests in scope (excludes REJECTED/CANCELLED/COMPLETED). NOT a stage sum.</summary>
    public int UniqueActiveRequests { get; set; }
    public List<OperationalPipelineStageDto> Stages { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}
