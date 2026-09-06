using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Application.DTOs.Dashboard;

// ── Dashboard V2 B9.4 — canonical Stage Aging read side (GERENCIAL, read-only). ──
// Measures how long in-scope operational entities have been in their CURRENT canonical stage, from the
// OperationalStageState snapshots (B9.2 live capture + B9.3 honest backfill). Age is time-in-stage, NEVER
// request age. Unknown age is first-class (null, never 0); only known-age entities contribute to severity.
// Buyer/REQUEST is out of scope (B9.2d); FIN_PAID/DRAFT/COMPLETED and terminal history codes are excluded.

/// <summary>Operational threshold profile for a stage. Never a formal SLA. Absent (null) = no severity.</summary>
public sealed class StageAgingThresholdProfileDto
{
    public int AttentionAfterDays { get; set; }  // age strictly greater → ATTENTION
    public int CriticalAfterDays { get; set; }   // age strictly greater → CRITICAL
    public bool IsFormalSla { get; set; }         // always false — operational guidance only
}

public sealed class DashboardV2StageAgingStageDto
{
    public string Domain { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;   // APPROVAL_BATCH | PO_GROUP
    public int SortOrder { get; set; }

    public int EntityCount { get; set; }
    public int RequestCount { get; set; }                     // distinct requests in this stage

    public int KnownAgeEntityCount { get; set; }
    public int UnknownAgeEntityCount { get; set; }

    // Severity counts are NULL for thresholdless stages (Finance/Documentation) so "not applicable" can
    // never be misread as zero. For thresholded stages they sum to KnownAgeEntityCount.
    public int? NormalCount { get; set; }
    public int? AttentionCount { get; set; }
    public int? CriticalCount { get; set; }

    public DateTime? OldestStageEnteredAtUtc { get; set; }    // oldest KNOWN-age entity only
    public int? OldestAgeDays { get; set; }

    public StageAgingThresholdProfileDto? ThresholdProfile { get; set; }

    public string? TargetPath { get; set; }
    public bool CanNavigate { get; set; }                     // false for managerial analytics in B9.4
}

public sealed class DashboardV2StageAgingSummaryDto
{
    public int TotalActiveEntities { get; set; }
    public int TotalActiveRequests { get; set; }              // distinct requests across ALL active stages
    public int KnownAgeEntities { get; set; }
    public int UnknownAgeEntities { get; set; }
    public int AttentionEntities { get; set; }                // known-age only, thresholded stages only
    public int CriticalEntities { get; set; }
}

public sealed class DashboardV2StageAgingDto
{
    /// <summary>Null when the caller is not entitled (Local Manager / SysAdmin) — the frontend hides the section.</summary>
    public DashboardV2StageAgingSummaryDto? Summary { get; set; }
    public List<DashboardV2StageAgingStageDto> Stages { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}

/// <summary>Severity classification of a KNOWN age against a threshold profile.</summary>
public enum StageAgingSeverity { Normal, Attention, Critical }

// The single authoritative catalog of ACTIVE B9 aging stages (Buyer / FIN_PAID / terminal excluded). One
// place — no scattered string comparisons. Codes/domains reuse the B6 PipelineStages / PipelineDomains
// vocabulary so the two never drift.
public sealed class StageAgingStageMeta
{
    public string Domain { get; init; } = string.Empty;
    public string StageCode { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public StageAgingThresholdProfileDto? Threshold { get; init; }
}

public static class StageAgingCatalog
{
    private static StageAgingThresholdProfileDto? Profile(int attention, int critical)
        => new() { AttentionAfterDays = attention, CriticalAfterDays = critical, IsFormalSla = false };

    // Ordered by B6 pipeline sort. Approval/PO = 3/7; Receiving = 7/14; Finance/Documentation = no severity.
    public static readonly IReadOnlyList<StageAgingStageMeta> ActiveStages = new List<StageAgingStageMeta>
    {
        new() { Domain = PipelineDomains.Aprovacoes, StageCode = PipelineStages.AreaApproval,  Label = "Aprovação de Área", EntityType = PipelineEntityTypes.ApprovalBatch, SortOrder = 30, Threshold = Profile(3, 7) },
        new() { Domain = PipelineDomains.Aprovacoes, StageCode = PipelineStages.FinalApproval, Label = "Aprovação Final",   EntityType = PipelineEntityTypes.ApprovalBatch, SortOrder = 31, Threshold = Profile(3, 7) },
        new() { Domain = PipelineDomains.Aprovacoes, StageCode = PipelineStages.Adjustment,    Label = "Reajuste",          EntityType = PipelineEntityTypes.ApprovalBatch, SortOrder = 32, Threshold = Profile(3, 7) },
        new() { Domain = PipelineDomains.Po,         StageCode = PipelineStages.PoWaiting,     Label = "Aguardando P.O.",   EntityType = PipelineEntityTypes.PoGroup,       SortOrder = 50, Threshold = Profile(3, 7) },
        new() { Domain = PipelineDomains.Po,         StageCode = PipelineStages.PoCorrection,  Label = "Correção de P.O.",  EntityType = PipelineEntityTypes.PoGroup,       SortOrder = 51, Threshold = Profile(3, 7) },
        new() { Domain = PipelineDomains.Financas,   StageCode = PipelineStages.FinanceNeedsScheduling, Label = "A agendar", EntityType = PipelineEntityTypes.PoGroup,      SortOrder = 60, Threshold = null },
        new() { Domain = PipelineDomains.Financas,   StageCode = PipelineStages.FinanceScheduled,       Label = "Agendado",  EntityType = PipelineEntityTypes.PoGroup,      SortOrder = 61, Threshold = null },
        new() { Domain = PipelineDomains.Recebimento, StageCode = PipelineStages.ReceivingReady,    Label = "Entrada em recebimento",   EntityType = PipelineEntityTypes.PoGroup, SortOrder = 70, Threshold = Profile(7, 14) },
        new() { Domain = PipelineDomains.Recebimento, StageCode = PipelineStages.ReceivingWaiting,  Label = "Aguardando recebimento",   EntityType = PipelineEntityTypes.PoGroup, SortOrder = 71, Threshold = Profile(7, 14) },
        new() { Domain = PipelineDomains.Recebimento, StageCode = PipelineStages.ReceivingFollowup, Label = "Acompanhamento",           EntityType = PipelineEntityTypes.PoGroup, SortOrder = 72, Threshold = Profile(7, 14) },
        new() { Domain = PipelineDomains.Recebimento, StageCode = PipelineStages.ReceivingSupplier, Label = "Aguardando fornecedor",    EntityType = PipelineEntityTypes.PoGroup, SortOrder = 73, Threshold = Profile(7, 14) },
        new() { Domain = PipelineDomains.Documentacao, StageCode = PipelineStages.Documentation,    Label = "Documentação fiscal",      EntityType = PipelineEntityTypes.PoGroup, SortOrder = 80, Threshold = null },
    };

    private static readonly Dictionary<string, StageAgingStageMeta> ByCode =
        ActiveStages.ToDictionary(s => s.StageCode);

    /// <summary>The active-stage codes — the authoritative B9.4 read population filter.</summary>
    public static readonly IReadOnlyCollection<string> ActiveStageCodes = ByCode.Keys.ToList();

    public static bool IsActive(string stageCode) => ByCode.ContainsKey(stageCode);
    public static StageAgingStageMeta? Meta(string stageCode) => ByCode.TryGetValue(stageCode, out var m) ? m : null;
}

/// <summary>Pure age + severity + ranking policy. Africa/Luanda calendar days (UTC+1, no DST).</summary>
public static class StageAgingPolicy
{
    private static readonly TimeSpan LuandaOffset = TimeSpan.FromHours(1); // WAT, no DST

    public static DateTime LuandaDate(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(LuandaOffset).Date;

    /// <summary>Calendar-day difference in Luanda, clamped at 0 (future/corrupt timestamps never go negative).</summary>
    public static int AgeDays(DateTime enteredAtUtc, DateTime nowUtc)
    {
        var days = (LuandaDate(nowUtc) - LuandaDate(enteredAtUtc)).Days;
        return days < 0 ? 0 : days;
    }

    public static StageAgingSeverity Classify(int ageDays, StageAgingThresholdProfileDto profile)
    {
        if (ageDays > profile.CriticalAfterDays) return StageAgingSeverity.Critical;
        if (ageDays > profile.AttentionAfterDays) return StageAgingSeverity.Attention;
        return StageAgingSeverity.Normal;
    }

    /// <summary>Bottleneck ranking for a future UI (NOT applied to the canonical API order): critical desc,
    /// then attention desc, then oldest age desc, then pipeline sort order.</summary>
    public static IReadOnlyList<DashboardV2StageAgingStageDto> RankByBottleneck(IEnumerable<DashboardV2StageAgingStageDto> stages)
        => stages
            .OrderByDescending(s => s.CriticalCount ?? 0)
            .ThenByDescending(s => s.AttentionCount ?? 0)
            .ThenByDescending(s => s.OldestAgeDays ?? -1)
            .ThenBy(s => s.SortOrder)
            .ToList();
}
