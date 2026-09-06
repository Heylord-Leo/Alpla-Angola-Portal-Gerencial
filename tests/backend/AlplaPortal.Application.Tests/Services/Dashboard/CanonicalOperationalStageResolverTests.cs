using System.Linq;
using System.Reflection;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.2 — the pure canonical stage resolver. Every mapping is a deterministic string→string
// map; a returned aging code is always a B6 PipelineStages value (taxonomy identity).
public class CanonicalOperationalStageResolverTests
{
    [Theory]
    [InlineData("WAITING_AREA_APPROVAL", "AREA_APPROVAL")]
    [InlineData("WAITING_FINAL_APPROVAL", "FINAL_APPROVAL")]
    [InlineData("AREA_ADJUSTMENT", "ADJUSTMENT")]
    [InlineData("FINAL_ADJUSTMENT", "ADJUSTMENT")]
    [InlineData("APPROVED", null)]   // batch done → not an active aging stage
    [InlineData("REJECTED", null)]
    [InlineData("CANCELLED", null)]
    public void Approval_batch_status_maps_to_canonical_stage(string status, string? expected)
        => Assert.Equal(expected, CanonicalOperationalStageResolver.ResolveApprovalBatchStage(status));

    [Theory]
    [InlineData("WAITING_PO", "PO_WAITING")]
    [InlineData("WAITING_PO_CORRECTION", "PO_CORRECTION")]
    [InlineData("PO_ISSUED", "FIN_NEEDS_SCHEDULING")]
    [InlineData("PAYMENT_REQUEST_SENT", "FIN_NEEDS_SCHEDULING")]
    [InlineData("ADVANCE_PAYMENT_REQUIRED", "FIN_NEEDS_SCHEDULING")]
    [InlineData("PAYMENT_SCHEDULED", "FIN_SCHEDULED")]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED", "FIN_SCHEDULED")]
    // EXCLUSIVE-AGING (B9.2b): payment completed → the clock belongs to Receiving (immediately actionable),
    // NOT Finance. So PAYMENT_COMPLETED → REC_READY, and there is no FIN_PAID aging snapshot.
    [InlineData("PAYMENT_COMPLETED", "REC_READY")]
    [InlineData("ADVANCE_PAYMENT_COMPLETED", null)] // transient advance marker → never a resting aging stage
    [InlineData("WAITING_RECEIPT", "REC_WAITING")]
    [InlineData("IN_FOLLOWUP", "REC_FOLLOWUP")]
    [InlineData("WAITING_SUPPLIER_DELIVERY", "REC_SUPPLIER")]
    [InlineData("WAITING_FISCAL_RECEIPT", "DOCUMENTATION")]
    [InlineData("WAITING_RECONCILIATION", "DOCUMENTATION")]
    [InlineData("PENDING", null)]     // pre-final-approval → not active
    [InlineData("COMPLETED", null)]   // terminal
    [InlineData("CANCELLED", null)]
    public void Po_group_status_maps_to_canonical_stage(string status, string? expected)
        => Assert.Equal(expected, CanonicalOperationalStageResolver.ResolvePoGroupStage(status));

    [Theory]
    [InlineData("NEEDS_QUOTATION", "NEEDS_QUOTATION")]
    [InlineData("PARTIAL_COVERAGE", "PARTIAL_COVERAGE")]
    [InlineData("READY_FOR_APPROVAL", "READY_FOR_APPROVAL")]
    [InlineData("AWAITING_APPROVAL", null)]  // left the buyer domain
    [InlineData("ADJUSTMENT_REQUIRED", null)]
    public void Buyer_operational_state_maps_to_canonical_stage(string state, string? expected)
        => Assert.Equal(expected, CanonicalOperationalStageResolver.ResolveBuyerStage(state));

    [Theory]
    [InlineData("COMPLETED", "COMPLETED")]
    [InlineData("APPROVED", "COMPLETED")]   // batch approved → its work is done
    [InlineData("CANCELLED", "CANCELLED")]
    [InlineData("REJECTED", "REJECTED")]
    [InlineData("PENDING", "EXITED")]       // out-of-scope / de-activated
    [InlineData("SOMETHING_ELSE", "EXITED")]
    public void Terminal_code_is_history_only_and_never_an_aging_stage(string raw, string expected)
    {
        var code = CanonicalOperationalStageResolver.ResolveTerminalCode(raw);
        Assert.Equal(expected, code);
        // A terminal code is never a member of the ACTIVE aging taxonomy (DRAFT/COMPLETED are excluded
        // from aging, so the terminal "COMPLETED" string does not collide with an aging stage).
        Assert.DoesNotContain(code, AgingStageCodes());
    }

    [Fact]
    public void Every_returned_aging_code_is_a_b6_pipeline_stage_code()
    {
        var b6 = AllPipelineStageCodes();
        string[] approvalStatuses = { "WAITING_AREA_APPROVAL", "WAITING_FINAL_APPROVAL", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT" };
        string[] groupStatuses =
        {
            "WAITING_PO", "WAITING_PO_CORRECTION", "PO_ISSUED", "PAYMENT_REQUEST_SENT", "ADVANCE_PAYMENT_REQUIRED",
            "PAYMENT_SCHEDULED", "ADVANCE_PAYMENT_SCHEDULED", "PAYMENT_COMPLETED",
            "WAITING_RECEIPT", "IN_FOLLOWUP", "WAITING_SUPPLIER_DELIVERY", "WAITING_FISCAL_RECEIPT", "WAITING_RECONCILIATION",
        };
        // Every NON-NULL resolved aging code must be a member of the shared B6 taxonomy.
        foreach (var s in approvalStatuses)
            Assert.Contains(CanonicalOperationalStageResolver.ResolveApprovalBatchStage(s)!, b6);
        foreach (var s in groupStatuses)
            Assert.Contains(CanonicalOperationalStageResolver.ResolvePoGroupStage(s)!, b6);
    }

    [Fact]
    public void Exclusive_aging_a_paid_group_ages_in_receiving_not_finance()
    {
        // The B6 pipeline may INFORMATIONALLY show a PAYMENT_COMPLETED group in BOTH Finanças (FIN_PAID) and
        // Recebimento (REC_READY) — overlap is intentional there. B9 aging is exclusive: the dwell owner is
        // Receiving, because Finance's action is finished and the group is immediately receiving-actionable.
        Assert.Equal(PipelineStages.ReceivingReady, CanonicalOperationalStageResolver.ResolvePoGroupStage("PAYMENT_COMPLETED"));
        Assert.NotEqual(PipelineStages.FinancePaid, CanonicalOperationalStageResolver.ResolvePoGroupStage("PAYMENT_COMPLETED"));
    }

    [Fact]
    public void Fin_paid_is_a_b6_informational_code_but_never_an_active_b9_aging_stage()
    {
        // FIN_PAID remains a valid B6 pipeline code (so DomainForStage still resolves it)...
        Assert.Contains(PipelineStages.FinancePaid, AllPipelineStageCodes());
        Assert.Equal(PipelineDomains.Financas, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.FinancePaid));
        // ...but NO PO-group status resolves to it — it is never a live aging snapshot stage.
        string[] allGroupStatuses =
        {
            "PENDING", "WAITING_PO", "WAITING_PO_CORRECTION", "ADVANCE_PAYMENT_REQUIRED", "ADVANCE_PAYMENT_SCHEDULED",
            "ADVANCE_PAYMENT_COMPLETED", "WAITING_SUPPLIER_DELIVERY", "PO_ISSUED", "PAYMENT_REQUEST_SENT",
            "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "WAITING_RECEIPT", "WAITING_RECONCILIATION", "IN_FOLLOWUP",
            "WAITING_FISCAL_RECEIPT", "COMPLETED", "CANCELLED",
        };
        Assert.DoesNotContain(allGroupStatuses.Select(CanonicalOperationalStageResolver.ResolvePoGroupStage),
            s => s == PipelineStages.FinancePaid);
    }

    [Fact]
    public void Domain_for_stage_matches_b6_domains()
    {
        Assert.Equal(PipelineDomains.Po, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.PoWaiting));
        Assert.Equal(PipelineDomains.Financas, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.FinanceScheduled));
        Assert.Equal(PipelineDomains.Recebimento, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.ReceivingWaiting));
        Assert.Equal(PipelineDomains.Aprovacoes, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.AreaApproval));
        Assert.Equal(PipelineDomains.Documentacao, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.Documentation));
        Assert.Equal(PipelineDomains.Compras, CanonicalOperationalStageResolver.DomainForStage(PipelineStages.NeedsQuotation));
    }

    private static string[] AllPipelineStageCodes()
        => typeof(PipelineStages)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    // Active aging stages = all pipeline stages except DRAFT and COMPLETED (excluded from B9 aging).
    private static string[] AgingStageCodes()
        => AllPipelineStageCodes().Where(c => c != PipelineStages.Draft && c != PipelineStages.Completed).ToArray();
}
