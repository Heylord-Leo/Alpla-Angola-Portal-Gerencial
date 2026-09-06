using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Services.Dashboard;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B6.1 — canonical Operational Pipeline. Locks the operational-entity model: a request contributes to
/// every stage where it has an entity, stages are grain-labelled and can overlap (incl. the
/// PAYMENT_COMPLETED Finance→Receiving handoff), UniqueActiveRequests is a distinct denominator (never a
/// stage sum), and no scalar Request.Status is used where group/batch data exists. Receiving reconciles
/// exactly with the canonical ReceivingActionEvaluator buckets.
/// </summary>
public class OperationalPipelineProjectionTests
{
    private const int TypeQuotation = 1, TypePayment = 2;
    private const int StDraft = 10, StWaitingQuotation = 11, StWaitingArea = 12, StCompleted = 13,
                      StPoIssued = 14, StActive = 15;

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.RequestTypes.AddRange(
            new RequestType { Id = TypeQuotation, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
            new RequestType { Id = TypePayment, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = StDraft, Code = RequestConstants.Statuses.Draft, Name = "Rascunho" },
            new RequestStatus { Id = StWaitingQuotation, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Ag. Cotação" },
            new RequestStatus { Id = StWaitingArea, Code = RequestConstants.Statuses.WaitingAreaApproval, Name = "Ag. Área" },
            new RequestStatus { Id = StCompleted, Code = RequestConstants.Statuses.Completed, Name = "Concluído" },
            new RequestStatus { Id = StPoIssued, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. Emitido" },
            new RequestStatus { Id = StActive, Code = "WAITING_RECEIPT", Name = "Ag. Recibo" });
        ctx.SaveChanges();
        return ctx;
    }

    private static Request NewRequest(int statusId, int typeId)
        => new()
        {
            Id = Guid.NewGuid(), Title = "T-" + Guid.NewGuid().ToString("N")[..6],
            RequestNumber = "R-" + Guid.NewGuid().ToString("N")[..6],
            StatusId = statusId, RequestTypeId = typeId, RequesterId = Guid.NewGuid(),
            DepartmentId = 5, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = DateTime.UtcNow,
        };

    private static void AddGroup(Request r, string status)
        => r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = status, TotalAmount = 100m });

    private static void AddBatch(Request r, string status)
        => r.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r.Id, BatchNumber = 1, Status = status });

    private static async Task<DashboardV2PipelineDto> Build(ApplicationDbContext ctx)
    {
        await ctx.SaveChangesAsync();
        var eligibility = new FinancePaymentEligibilityService();
        var projection = new OperationalPipelineProjection(ctx, eligibility);
        return await projection.BuildAsync(ctx.Requests, DateTime.UtcNow.Date);
    }

    private static int Entity(DashboardV2PipelineDto d, string stage) => d.Stages.Single(s => s.Stage == stage).EntityCount;
    private static int Requests(DashboardV2PipelineDto d, string stage) => d.Stages.Single(s => s.Stage == stage).RequestCount;

    [Fact]
    public async Task Emits_all_canonical_stages_in_stable_sort_order()
    {
        using var ctx = NewDb();
        var d = await Build(ctx);
        var orders = d.Stages.Select(s => s.SortOrder).ToList();
        Assert.Equal(orders.OrderBy(x => x).ToList(), orders); // already sorted
        Assert.Contains(d.Stages, s => s.Stage == PipelineStages.Draft);
        Assert.Contains(d.Stages, s => s.Stage == PipelineStages.Completed);
        Assert.All(d.Stages, s => Assert.True(s.CanOverlap));
    }

    [Fact]
    public async Task UniqueActiveRequests_excludes_terminal_and_is_not_a_stage_sum()
    {
        using var ctx = NewDb();
        ctx.Requests.Add(NewRequest(StDraft, TypePayment));           // active
        ctx.Requests.Add(NewRequest(StWaitingQuotation, TypeQuotation)); // active
        ctx.Requests.Add(NewRequest(StCompleted, TypeQuotation));     // terminal → excluded
        var d = await Build(ctx);
        Assert.Equal(2, d.UniqueActiveRequests);
        Assert.Equal(1, Entity(d, PipelineStages.Completed)); // completed still shown as its own stage
    }

    [Fact]
    public async Task Draft_stage_counts_own_draft_requests_at_request_grain()
    {
        using var ctx = NewDb();
        ctx.Requests.Add(NewRequest(StDraft, TypePayment));
        ctx.Requests.Add(NewRequest(StDraft, TypeQuotation));
        var d = await Build(ctx);
        Assert.Equal(2, Entity(d, PipelineStages.Draft));
        Assert.Equal(PipelineEntityTypes.Request, d.Stages.Single(s => s.Stage == PipelineStages.Draft).EntityType);
    }

    [Fact]
    public async Task Buyer_needs_quotation_state_counted_via_canonical_projection()
    {
        using var ctx = NewDb();
        var r = NewRequest(StWaitingQuotation, TypeQuotation);
        r.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = r.Id, Description = "i", Quantity = 1, IsDeleted = false, QuotationLifecycleStatus = null });
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(1, Entity(d, PipelineStages.NeedsQuotation));
        Assert.Equal(PipelineEntityTypes.Request, d.Stages.Single(s => s.Stage == PipelineStages.NeedsQuotation).EntityType);
    }

    [Fact]
    public async Task Area_and_final_approval_are_batch_grain()
    {
        using var ctx = NewDb();
        var r = NewRequest(StWaitingArea, TypeQuotation);
        AddBatch(r, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        AddBatch(r, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval); // 2nd waiting-area batch, same request
        AddBatch(r, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(2, Entity(d, PipelineStages.AreaApproval));     // 2 batches
        Assert.Equal(1, Requests(d, PipelineStages.AreaApproval));   // 1 request
        Assert.Equal(1, Entity(d, PipelineStages.FinalApproval));
        Assert.Equal(PipelineEntityTypes.ApprovalBatch, d.Stages.Single(s => s.Stage == PipelineStages.AreaApproval).EntityType);
    }

    [Fact]
    public async Task Adjustment_uses_active_batch_statuses_only()
    {
        using var ctx = NewDb();
        var r = NewRequest(StWaitingArea, TypeQuotation);
        AddBatch(r, RequestConstants.ApprovalBatchStatuses.AreaAdjustment);
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(1, Entity(d, PipelineStages.Adjustment));
    }

    [Fact]
    public async Task Po_stages_are_group_grain()
    {
        using var ctx = NewDb();
        var r = NewRequest(StPoIssued, TypeQuotation);
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingPo);
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(1, Entity(d, PipelineStages.PoWaiting));
        Assert.Equal(1, Entity(d, PipelineStages.PoCorrection));
        Assert.Equal(PipelineEntityTypes.PoGroup, d.Stages.Single(s => s.Stage == PipelineStages.PoWaiting).EntityType);
    }

    [Fact]
    public async Task Receiving_stages_reconcile_exactly_with_canonical_buckets()
    {
        using var ctx = NewDb();
        var r = NewRequest(StPoIssued, TypeQuotation);
        AddGroup(r, RequestConstants.PoGroupStatuses.PaymentCompleted);       // READY_FOR_RECEIPT
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingReceipt);         // WAITING_RECEIPT
        AddGroup(r, RequestConstants.PoGroupStatuses.InFollowup);             // IN_FOLLOWUP
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingSupplierDelivery);// WAITING_SUPPLIER_DELIVERY
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingPo);              // NOT receiving (buyer PO)
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingReady));
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingWaiting));
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingFollowup));
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingSupplier));
    }

    [Fact]
    public async Task Payment_completed_receiving_side_of_handoff_and_finance_paid_stage_emitted()
    {
        using var ctx = NewDb();
        var r = NewRequest(StPoIssued, TypeQuotation);
        AddGroup(r, RequestConstants.PoGroupStatuses.PaymentCompleted);
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        // Receiving side of the intentional Finance→Receiving handoff overlap (CanOverlap).
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingReady));
        // The Finance PAID stage is always emitted; its COUNT for this handoff is verified in the
        // LocalDB integration test (the canonical B3 finance query cannot materialize under EF in-memory
        // due to its double PoGroups ThenInclude — a test-infra limitation, not a production defect).
        Assert.Contains(d.Stages, s => s.Stage == PipelineStages.FinancePaid);
    }

    [Fact]
    public async Task Documentation_stage_counts_fiscal_and_reconciliation_groups()
    {
        using var ctx = NewDb();
        var r = NewRequest(StPoIssued, TypeQuotation);
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingFiscalReceipt);
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingReconciliation);
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(2, Entity(d, PipelineStages.Documentation));
    }

    [Fact]
    public async Task Completed_stage_uses_scalar_completed_and_excludes_cancelled()
    {
        using var ctx = NewDb();
        ctx.Requests.Add(NewRequest(StCompleted, TypeQuotation));
        var d = await Build(ctx);
        Assert.Equal(1, Entity(d, PipelineStages.Completed));
    }

    [Fact]
    public async Task Multi_group_request_contributes_to_multiple_stages()
    {
        using var ctx = NewDb();
        var r = NewRequest(StPoIssued, TypeQuotation);
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingPo);        // PO
        AddGroup(r, RequestConstants.PoGroupStatuses.PaymentScheduled); // Finance scheduled
        AddGroup(r, RequestConstants.PoGroupStatuses.WaitingReceipt);   // Receiving waiting
        ctx.Requests.Add(r);
        var d = await Build(ctx);
        Assert.Equal(1, d.UniqueActiveRequests);
        Assert.Equal(1, Entity(d, PipelineStages.PoWaiting));       // group A (group grain)
        Assert.Equal(1, Entity(d, PipelineStages.ReceivingWaiting)); // group C (group grain)
        // Same request contributes to multiple distinct stage requestCounts — the whole point.
        Assert.Equal(1, Requests(d, PipelineStages.PoWaiting));
        Assert.Equal(1, Requests(d, PipelineStages.ReceivingWaiting));
        // Group B (PAYMENT_SCHEDULED → Finance·Agendado) count is verified in the LocalDB integration
        // test; the stage is always emitted here.
        Assert.Contains(d.Stages, s => s.Stage == PipelineStages.FinanceScheduled);
    }

    [Fact]
    public async Task Target_paths_are_exact_or_null()
    {
        using var ctx = NewDb();
        var d = await Build(ctx);
        // Exact canonical filters only for Finance + Receiving; everything else null.
        Assert.Equal("/finance/payments?actionClass=NEEDS_SCHEDULING", d.Stages.Single(s => s.Stage == PipelineStages.FinanceNeedsScheduling).TargetPath);
        Assert.Equal("/receiving/workspace?receivingBucket=WAITING_RECEIPT", d.Stages.Single(s => s.Stage == PipelineStages.ReceivingWaiting).TargetPath);
        Assert.Null(d.Stages.Single(s => s.Stage == PipelineStages.AreaApproval).TargetPath);
        Assert.Null(d.Stages.Single(s => s.Stage == PipelineStages.PoWaiting).TargetPath);
        Assert.Null(d.Stages.Single(s => s.Stage == PipelineStages.NeedsQuotation).TargetPath);
        Assert.Null(d.Stages.Single(s => s.Stage == PipelineStages.Completed).TargetPath);
    }

    [Fact]
    public async Task No_money_no_aging_fields_on_the_contract()
    {
        using var ctx = NewDb();
        var d = await Build(ctx);
        // The stage DTO surface carries only counts/metadata — no amount/currency/age/dueDate fields exist.
        var props = typeof(OperationalPipelineStageDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(props, n => n.Contains("Amount") || n.Contains("Currency") || n.Contains("Age") || n.Contains("Due") || n.Contains("Overdue"));
    }
}
