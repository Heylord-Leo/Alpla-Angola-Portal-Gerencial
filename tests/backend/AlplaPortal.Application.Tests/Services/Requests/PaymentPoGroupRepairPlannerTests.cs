using AlplaPortal.Domain.Services;
using Xunit;
using static AlplaPortal.Domain.Services.PaymentPoGroupRepairPlanner;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Phase 4B.2 — pure classification rules for the historical PAYMENT PO-group repair. No database:
/// the planner only weighs already-gathered facts, so every branch of the SAFE/MANUAL/SKIP verdict
/// can be pinned here.
/// </summary>
public class PaymentPoGroupRepairPlannerTests
{
    // A healthy multi-document candidate: APPROVED payment, final-approved, no groups, documents with
    // linked items, a supplier, no downstream artefacts.
    private static Input MultiDoc(
        string type = "PAYMENT", string status = "APPROVED", bool approved = true,
        int groups = 0, int docs = 2, int items = 3, int linked = 3,
        bool supplier = true, bool downstream = false) =>
        new(type, status, approved, groups, docs, items, linked, supplier, downstream);

    [Fact]
    public void A_MultiDocument_WithLinkedItems_IsSafeToRepair()
    {
        var a = Assess(MultiDoc());
        Assert.Equal(Classification.SafeToRepair, a.Verdict);
        Assert.Equal(Model.MultiDocument, a.Model);
    }

    [Fact]
    public void B_LegacyHeader_WithSupplier_IsSafeToRepair()
    {
        var a = Assess(MultiDoc(docs: 0, items: 1, linked: 0, supplier: true));
        Assert.Equal(Classification.SafeToRepair, a.Verdict);
        Assert.Equal(Model.LegacyHeader, a.Model);
    }

    [Fact]
    public void D_AlreadyHasGroups_IsSkipped()
    {
        var a = Assess(MultiDoc(groups: 1));
        Assert.Equal(Classification.Skip, a.Verdict);
    }

    [Fact]
    public void E_DownstreamEvidenceWithoutGroups_IsManualReview()
    {
        var a = Assess(MultiDoc(downstream: true));
        Assert.Equal(Classification.ManualReview, a.Verdict);
    }

    [Fact]
    public void F_DocumentsButNoLinkedItems_IsManualReview()
    {
        var a = Assess(MultiDoc(docs: 2, items: 2, linked: 0));
        Assert.Equal(Classification.ManualReview, a.Verdict);
        Assert.Equal(Model.MultiDocument, a.Model);
    }

    [Fact]
    public void G_NonApprovedStatus_IsSkipped()
    {
        Assert.Equal(Classification.Skip, Assess(MultiDoc(status: "CANCELLED")).Verdict);
        Assert.Equal(Classification.Skip, Assess(MultiDoc(status: "WAITING_FINAL_APPROVAL")).Verdict);
    }

    [Fact]
    public void G_NotFinalApproved_IsSkipped()
    {
        Assert.Equal(Classification.Skip, Assess(MultiDoc(approved: false)).Verdict);
    }

    [Fact]
    public void H_QuotationRequest_IsSkipped_NeverTouched()
    {
        var a = Assess(MultiDoc(type: "QUOTATION"));
        Assert.Equal(Classification.Skip, a.Verdict);
    }

    [Fact]
    public void LegacyHeader_WithoutSupplier_IsManualReview()
    {
        var a = Assess(MultiDoc(docs: 0, items: 1, linked: 0, supplier: false));
        Assert.Equal(Classification.ManualReview, a.Verdict);
        Assert.Equal(Model.LegacyHeader, a.Model);
    }

    [Fact]
    public void SkipOrder_ExistingGroupsBeatsDownstream()
    {
        // A request that both has groups AND downstream evidence is a no-op (already has groups),
        // never a manual-review — the "nothing to do" verdict must win to avoid false alarms.
        var a = Assess(MultiDoc(groups: 2, downstream: true));
        Assert.Equal(Classification.Skip, a.Verdict);
    }
}
