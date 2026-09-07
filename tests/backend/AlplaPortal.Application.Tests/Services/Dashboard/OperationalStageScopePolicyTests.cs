using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 B9.2d — scope-closure policy guards. Buyer/REQUEST aging is formally OUT OF SCOPE for this
/// B9 release: the active aging grains are APPROVAL_BATCH and PO_GROUP only. These tests lock that policy so
/// no accident re-introduces Buyer aging, keeps FIN_PAID out of the active taxonomy, and confirms the
/// dormant Buyer resolver is never wired into live capture. REQUEST remains a schema capability for a future
/// release (not removed), but produces no current B9 snapshots.
/// </summary>
public class OperationalStageScopePolicyTests
{
    // The complete set of ACTIVE B9 aging stages for this release (no Buyer, no FIN_PAID).
    private static readonly string[] ActiveAgingStages =
    {
        PipelineStages.AreaApproval, PipelineStages.FinalApproval, PipelineStages.Adjustment,
        PipelineStages.PoWaiting, PipelineStages.PoCorrection,
        PipelineStages.FinanceNeedsScheduling, PipelineStages.FinanceScheduled,
        PipelineStages.ReceivingReady, PipelineStages.ReceivingWaiting,
        PipelineStages.ReceivingFollowup, PipelineStages.ReceivingSupplier,
        PipelineStages.Documentation,
    };

    private static readonly string[] BuyerStages =
    {
        PipelineStages.NeedsQuotation, PipelineStages.PartialCoverage, PipelineStages.ReadyForApproval,
    };

    private static readonly string[] AllApprovalBatchStatuses =
    {
        "WAITING_AREA_APPROVAL", "WAITING_FINAL_APPROVAL", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT",
        "APPROVED", "REJECTED", "CANCELLED",
    };

    private static readonly string[] AllPoGroupStatuses =
    {
        "PENDING", "WAITING_PO", "WAITING_PO_CORRECTION", "ADVANCE_PAYMENT_REQUIRED", "ADVANCE_PAYMENT_SCHEDULED",
        "ADVANCE_PAYMENT_COMPLETED", "WAITING_SUPPLIER_DELIVERY", "PO_ISSUED", "PAYMENT_REQUEST_SENT",
        "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "WAITING_RECEIPT", "WAITING_RECONCILIATION", "IN_FOLLOWUP",
        "WAITING_FISCAL_RECEIPT", "COMPLETED", "CANCELLED",
    };

    // ── The two live grains only ever resolve into the active taxonomy (or null). ──
    [Fact]
    public void Active_grains_only_resolve_into_the_active_aging_taxonomy()
    {
        foreach (var s in AllApprovalBatchStatuses)
        {
            var stage = CanonicalOperationalStageResolver.ResolveApprovalBatchStage(s);
            if (stage != null) Assert.Contains(stage, ActiveAgingStages);
        }
        foreach (var s in AllPoGroupStatuses)
        {
            var stage = CanonicalOperationalStageResolver.ResolvePoGroupStage(s);
            if (stage != null) Assert.Contains(stage, ActiveAgingStages);
        }
    }

    [Fact]
    public void Buyer_stages_have_no_current_b9_aging_counterpart_via_the_active_grains()
    {
        // No APPROVAL_BATCH or PO_GROUP status may resolve to a Buyer stage — Buyer never enters the
        // in-scope B9 population, so it can never contribute unknown-age counts either.
        foreach (var s in AllApprovalBatchStatuses)
            Assert.DoesNotContain(CanonicalOperationalStageResolver.ResolveApprovalBatchStage(s), BuyerStages);
        foreach (var s in AllPoGroupStatuses)
            Assert.DoesNotContain(CanonicalOperationalStageResolver.ResolvePoGroupStage(s), BuyerStages);
    }

    [Fact]
    public void Fin_paid_is_not_in_the_active_aging_taxonomy()
        => Assert.DoesNotContain(PipelineStages.FinancePaid, ActiveAgingStages);

    [Fact]
    public void Payment_completed_resolves_to_receiving_ready_not_finance()
        => Assert.Equal(PipelineStages.ReceivingReady, CanonicalOperationalStageResolver.ResolvePoGroupStage("PAYMENT_COMPLETED"));

    [Fact]
    public void Buyer_codes_remain_valid_b6_pipeline_stages()
    {
        // Descoping from B9 aging does NOT remove them from the B6 pipeline taxonomy.
        Assert.Equal("NEEDS_QUOTATION", PipelineStages.NeedsQuotation);
        Assert.Equal("PARTIAL_COVERAGE", PipelineStages.PartialCoverage);
        Assert.Equal("READY_FOR_APPROVAL", PipelineStages.ReadyForApproval);
    }

    // ── The dormant Buyer resolver exists and is tested, but is NEVER wired into live capture. ──
    [Fact]
    public void Buyer_resolver_is_marked_future_not_active_and_is_never_called_by_capture()
    {
        var resolver = Source("Infrastructure", "Services", "Dashboard", "CanonicalOperationalStageResolver.cs");
        Assert.Contains("FUTURE / NOT ACTIVE", resolver);

        // No capture code path references the dormant Buyer resolver.
        var tracker = Source("Infrastructure", "Services", "Dashboard", "OperationalStageTracker.cs");
        var dbContext = Source("Infrastructure", "Data", "ApplicationDbContext.cs");
        Assert.DoesNotContain("ResolveBuyerStage", tracker);
        Assert.DoesNotContain("ResolveBuyerStage", dbContext);
    }

    [Fact]
    public void Capture_processes_only_the_two_lot_grains_and_adds_no_buyer_graph_load()
    {
        var tracker = Source("Infrastructure", "Services", "Dashboard", "OperationalStageTracker.cs");
        // Only the two lot grains are inspected in the ChangeTracker.
        Assert.Contains("Entries<RequestPoGroup>", tracker);
        Assert.Contains("Entries<ApprovalBatch>", tracker);
        Assert.DoesNotContain("Entries<Request>(", tracker); // never the REQUEST/Buyer grain
        // No Buyer/quotation hydrate was introduced solely for B9 capture.
        var dbContext = Source("Infrastructure", "Data", "ApplicationDbContext.cs");
        Assert.DoesNotContain("BuyerQueueProjection", dbContext);
        Assert.DoesNotContain(".Include(r => r.Quotations", dbContext);
    }

    private static string Source(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var full = Path.Combine(new[] { dir!.FullName, "src", "backend", "AlplaPortal." + relativeParts[0] }
            .Concat(relativeParts.Skip(1)).ToArray());
        return File.ReadAllText(full);
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
