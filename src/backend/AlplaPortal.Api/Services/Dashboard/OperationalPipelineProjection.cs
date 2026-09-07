using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Receiving;
using Microsoft.EntityFrameworkCore;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;

namespace AlplaPortal.Api.Services.Dashboard;

/// <summary>
/// Dashboard V2 B6 — canonical Operational Pipeline (GERENCIAL, read-only). Composes the SAME canonical
/// domain sources the operational screens use — it never re-derives any status/action predicate:
///   • Compras   → <see cref="BuyerQueueProjectionBuilder"/> operational state (NeedsQuotation /
///                 PartialCoverage / ReadyForApproval). Buyer AdjustmentRequired / AwaitingApproval are
///                 NOT counted here — those are represented once, canonically, by the Adjustment and
///                 Area/Final batch stages (no double count).
///   • Aprovações→ ApprovalBatch.Status (WAITING_AREA_APPROVAL / WAITING_FINAL_APPROVAL /
///                 AREA_ADJUSTMENT|FINAL_ADJUSTMENT). Batch grain; workflow presence, not ownership.
///   • P.O.      → RequestPoGroup.Status (WAITING_PO / WAITING_PO_CORRECTION). Group grain.
///   • Finanças  → <see cref="FinanceObligationSummaryProjection"/> ActionClass → flow position
///                 (NEEDS_SCHEDULING / SCHEDULED / PAID). Group grain. Overdue is NOT a stage (PD-B6-04).
///   • Recebimento→ <see cref="ReceivingQueueProjection"/> canonical buckets. Group grain (exact = B4).
///   • Documentação→ group WAITING_FISCAL_RECEIPT / WAITING_RECONCILIATION antechamber.
///   • Conclusão → request scalar COMPLETED — authored ONLY by RequestCompletionService when every
///                 non-cancelled group is complete, so the scalar IS the canonical all-groups result.
///
/// A request may appear in several stages (CanOverlap). The PAYMENT_COMPLETED handoff intentionally
/// shows in Finanças·Pago AND Recebimento·Entrada. No aging, no money, no urgency, no alerts.
/// </summary>
public sealed class OperationalPipelineProjection
{
    private readonly ApplicationDbContext _context;
    private readonly IFinancePaymentEligibilityService _eligibility;

    public OperationalPipelineProjection(ApplicationDbContext context, IFinancePaymentEligibilityService eligibility)
    {
        _context = context;
        _eligibility = eligibility;
    }

    // Terminal request statuses — excluded from UniqueActiveRequests (mirrors the legacy cockpit set).
    private static readonly string[] TerminalRequestStatuses =
    {
        RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Completed,
    };

    // Buyer-active hydration bound — identical to the Buyer queue / Dashboard Buyer section.
    private static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    public async Task<DashboardV2PipelineDto> BuildAsync(IQueryable<Request> scoped, DateTime today)
    {
        var stages = new List<OperationalPipelineStageDto>();

        var uniqueActive = await scoped
            .Where(r => !TerminalRequestStatuses.Contains(r.Status!.Code))
            .Select(r => r.Id).Distinct().CountAsync();

        stages.Add(await RequestStageAsync(scoped, PipelineDomains.Preparacao, PipelineStages.Draft,
            "Rascunho", 10, r => r.Status!.Code == RequestConstants.Statuses.Draft, targetPath: null));

        stages.AddRange(await BuyerStagesAsync(scoped, today));
        stages.AddRange(await ApprovalStagesAsync(scoped));
        stages.AddRange(await PoStagesAsync(scoped));
        stages.AddRange(await FinanceStagesAsync(scoped, today));
        stages.AddRange(await ReceivingStagesAsync(scoped));
        stages.Add(await DocumentationStageAsync(scoped));

        stages.Add(await RequestStageAsync(scoped, PipelineDomains.Conclusao, PipelineStages.Completed,
            "Concluído", 90, r => r.Status!.Code == RequestConstants.Statuses.Completed, targetPath: null));

        return new DashboardV2PipelineDto
        {
            UniqueActiveRequests = uniqueActive,
            Stages = stages.OrderBy(s => s.SortOrder).ToList(),
            GeneratedAtUtc = DateTime.UtcNow,
        };
    }

    // ── Request-grain stage (Draft / Completed) ──
    private async Task<OperationalPipelineStageDto> RequestStageAsync(
        IQueryable<Request> scoped, string domain, string stage, string label, int sort,
        System.Linq.Expressions.Expression<Func<Request, bool>> predicate, string? targetPath)
    {
        var count = await scoped.Where(predicate).Select(r => r.Id).Distinct().CountAsync();
        return new OperationalPipelineStageDto
        {
            Domain = domain, Stage = stage, Label = label, EntityType = PipelineEntityTypes.Request,
            EntityCount = count, RequestCount = count, SortOrder = sort, TargetPath = targetPath,
        };
    }

    // ── COMPRAS: canonical Buyer operational states (reuses BuyerQueueProjectionBuilder). ──
    private async Task<List<OperationalPipelineStageDto>> BuyerStagesAsync(IQueryable<Request> scoped, DateTime today)
    {
        var requests = await scoped
            .Where(r => r.RequestType!.Code == RequestConstants.Types.Quotation
                        && BuyerActiveRequestStatusCodes.Contains(r.Status!.Code))
            .Include(r => r.RequestType).Include(r => r.Status).Include(r => r.NeedLevel).Include(r => r.Buyer)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(r => r.PoGroups)
            .Include(r => r.Quotations).ThenInclude(qq => qq.Items)
            .Include(r => r.Attachments)
            .AsSplitQuery().AsNoTracking()
            .ToListAsync();

        var byState = new Dictionary<string, HashSet<Guid>>();
        foreach (var r in requests)
        {
            var p = Proj.Build(BuyerQueueProjectionInputFactory.FromRequest(r), Guid.Empty, today);
            if (!byState.TryGetValue(p.OperationalState, out var set)) byState[p.OperationalState] = set = new HashSet<Guid>();
            set.Add(r.Id);
        }

        int Count(string state) => byState.TryGetValue(state, out var s) ? s.Count : 0;

        return new List<OperationalPipelineStageDto>
        {
            BuyerStage(PipelineStages.NeedsQuotation, "Sem cotação", 20, Count(BuyerQueueConstants.OperationalStates.NeedsQuotation)),
            BuyerStage(PipelineStages.PartialCoverage, "Cobertura parcial", 21, Count(BuyerQueueConstants.OperationalStates.PartialCoverage)),
            BuyerStage(PipelineStages.ReadyForApproval, "Pronto para aprovação", 22, Count(BuyerQueueConstants.OperationalStates.ReadyForApproval)),
        };
    }

    private static OperationalPipelineStageDto BuyerStage(string stage, string label, int sort, int count) => new()
    {
        Domain = PipelineDomains.Compras, Stage = stage, Label = label, EntityType = PipelineEntityTypes.Request,
        EntityCount = count, RequestCount = count, SortOrder = sort, TargetPath = null, // no exact per-state /buyer/items filter
    };

    // ── APROVAÇÕES: ApprovalBatch grain (batches + distinct requests). Workflow presence, not ownership. ──
    private async Task<List<OperationalPipelineStageDto>> ApprovalStagesAsync(IQueryable<Request> scoped)
    {
        var pairs = await scoped
            .SelectMany(r => r.ApprovalBatches, (r, b) => new { r.Id, b.Status })
            .Where(x => x.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval
                        || x.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval
                        || x.Status == RequestConstants.ApprovalBatchStatuses.AreaAdjustment
                        || x.Status == RequestConstants.ApprovalBatchStatuses.FinalAdjustment)
            .ToListAsync();

        OperationalPipelineStageDto Stage(string stage, string label, int sort, Func<string, bool> match) => new()
        {
            Domain = PipelineDomains.Aprovacoes, Stage = stage, Label = label, EntityType = PipelineEntityTypes.ApprovalBatch,
            EntityCount = pairs.Count(x => match(x.Status)),
            RequestCount = pairs.Where(x => match(x.Status)).Select(x => x.Id).Distinct().Count(),
            SortOrder = sort, TargetPath = null, // /approvals has no exact per-stage filter
        };

        return new List<OperationalPipelineStageDto>
        {
            Stage(PipelineStages.AreaApproval, "Aprovação de Área", 30, s => s == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval),
            Stage(PipelineStages.FinalApproval, "Aprovação Final", 31, s => s == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval),
            Stage(PipelineStages.Adjustment, "Reajuste", 32, s => s == RequestConstants.ApprovalBatchStatuses.AreaAdjustment || s == RequestConstants.ApprovalBatchStatuses.FinalAdjustment),
        };
    }

    // ── P.O.: RequestPoGroup grain (groups + distinct requests). ──
    private async Task<List<OperationalPipelineStageDto>> PoStagesAsync(IQueryable<Request> scoped)
    {
        var pairs = await scoped
            .SelectMany(r => r.PoGroups, (r, g) => new { r.Id, g.Status })
            .Where(x => x.Status == RequestConstants.PoGroupStatuses.WaitingPo
                        || x.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection)
            .ToListAsync();

        OperationalPipelineStageDto Stage(string stage, string label, int sort, string status) => new()
        {
            Domain = PipelineDomains.Po, Stage = stage, Label = label, EntityType = PipelineEntityTypes.PoGroup,
            EntityCount = pairs.Count(x => x.Status == status),
            RequestCount = pairs.Where(x => x.Status == status).Select(x => x.Id).Distinct().Count(),
            SortOrder = sort, TargetPath = null, // no exact group-status filter route
        };

        return new List<OperationalPipelineStageDto>
        {
            Stage(PipelineStages.PoWaiting, "Aguardando P.O.", 50, RequestConstants.PoGroupStatuses.WaitingPo),
            Stage(PipelineStages.PoCorrection, "Correção de P.O.", 51, RequestConstants.PoGroupStatuses.WaitingPoCorrection),
        };
    }

    // ── FINANÇAS: canonical eligibility ActionClass → flow position (no urgency, no money). ──
    private async Task<List<OperationalPipelineStageDto>> FinanceStagesAsync(IQueryable<Request> scoped, DateTime today)
    {
        var built = await new FinanceObligationSummaryProjection(_eligibility).BuildAsync(scoped, null, null, null, today);

        // Each obligation (one per non-cancelled group) has exactly one ActionClass → mutually exclusive.
        (int e, int r) Pos(string actionClass)
        {
            var rows = built.Obligations.Where(o => o.ActionClass == actionClass).ToList();
            return (rows.Count, rows.Select(o => o.RequestId).Distinct().Count());
        }

        var sched = Pos(FinanceActionClasses.NeedsScheduling);
        var scheduled = Pos(FinanceActionClasses.NeedsPayment);           // scheduled, awaiting payment confirmation
        var paid = Pos(FinanceActionClasses.PaidWaitingReceiving);        // paid → handed off to receiving

        OperationalPipelineStageDto Stage(string stage, string label, int sort, (int e, int r) c, string? target) => new()
        {
            Domain = PipelineDomains.Financas, Stage = stage, Label = label, EntityType = PipelineEntityTypes.PoGroup,
            EntityCount = c.e, RequestCount = c.r, SortOrder = sort, TargetPath = target,
        };

        return new List<OperationalPipelineStageDto>
        {
            Stage(PipelineStages.FinanceNeedsScheduling, "A agendar", 60, sched, "/finance/payments?actionClass=NEEDS_SCHEDULING"),
            Stage(PipelineStages.FinanceScheduled, "Agendado", 61, scheduled, "/finance/payments?actionClass=NEEDS_PAYMENT"),
            Stage(PipelineStages.FinancePaid, "Pago", 62, paid, "/finance/payments?actionClass=PAID_WAITING_RECEIVING"),
        };
    }

    // ── RECEBIMENTO: canonical ReceivingQueueProjection buckets (exact reconciliation with B4). ──
    private async Task<List<OperationalPipelineStageDto>> ReceivingStagesAsync(IQueryable<Request> scoped)
    {
        var built = await new ReceivingQueueProjection().BuildAsync(scoped);

        (int e, int r) Bucket(string bucket)
        {
            var rows = built.Rows.Where(x => x.ActionableBucket == bucket).ToList();
            return (rows.Count, rows.Select(x => x.RequestId).Distinct().Count());
        }

        OperationalPipelineStageDto Stage(string stage, string label, int sort, (int e, int r) c, string bucket) => new()
        {
            Domain = PipelineDomains.Recebimento, Stage = stage, Label = label, EntityType = PipelineEntityTypes.PoGroup,
            EntityCount = c.e, RequestCount = c.r, SortOrder = sort,
            TargetPath = $"/receiving/workspace?receivingBucket={bucket}",
        };

        return new List<OperationalPipelineStageDto>
        {
            Stage(PipelineStages.ReceivingReady, "Entrada em recebimento", 70, Bucket(ReceivingActionEvaluator.Buckets.ReadyForReceipt), ReceivingActionEvaluator.Buckets.ReadyForReceipt),
            Stage(PipelineStages.ReceivingWaiting, "Aguardando recebimento", 71, Bucket(ReceivingActionEvaluator.Buckets.WaitingReceipt), ReceivingActionEvaluator.Buckets.WaitingReceipt),
            Stage(PipelineStages.ReceivingFollowup, "Acompanhamento", 72, Bucket(ReceivingActionEvaluator.Buckets.FollowUp), ReceivingActionEvaluator.Buckets.FollowUp),
            Stage(PipelineStages.ReceivingSupplier, "Aguardando fornecedor", 73, Bucket(ReceivingActionEvaluator.Buckets.WaitingSupplierDelivery), ReceivingActionEvaluator.Buckets.WaitingSupplierDelivery),
        };
    }

    // ── DOCUMENTAÇÃO FISCAL: group antechamber before completion. ──
    private async Task<OperationalPipelineStageDto> DocumentationStageAsync(IQueryable<Request> scoped)
    {
        var pairs = await scoped
            .SelectMany(r => r.PoGroups, (r, g) => new { r.Id, g.Status })
            .Where(x => x.Status == RequestConstants.PoGroupStatuses.WaitingFiscalReceipt
                        || x.Status == RequestConstants.PoGroupStatuses.WaitingReconciliation)
            .ToListAsync();

        return new OperationalPipelineStageDto
        {
            Domain = PipelineDomains.Documentacao, Stage = PipelineStages.Documentation, Label = "Documentação fiscal",
            EntityType = PipelineEntityTypes.PoGroup,
            EntityCount = pairs.Count,
            RequestCount = pairs.Select(x => x.Id).Distinct().Count(),
            SortOrder = 80, TargetPath = null,
        };
    }
}
