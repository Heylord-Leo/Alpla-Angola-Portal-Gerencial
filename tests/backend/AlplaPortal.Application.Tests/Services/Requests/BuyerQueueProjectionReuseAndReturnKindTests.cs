using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using Xunit;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;
using S = AlplaPortal.Domain.Constants.BuyerQueueConstants;
using K = AlplaPortal.Domain.Constants.PoGroupCancellationConstants.FinanceReturnKinds;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.2 — (1) a released line whose only candidates are REUSE-BLOCKED (historical quotation of the cancelled
/// P.O., no authorization) is "Pendente de cotação", never "Pronto para lote": header, counters, line state, next
/// actions and the submission modal agree (TEST finding on REQ-28/10/2026-628); (2) PO_CORRECTION label and next
/// actions follow the STORED Finance return kind; (3) a group awaiting its FIRST P.O. is "P.O. a registar", not
/// "Concluído para Compras", without becoming queue/alert work.
/// </summary>
public class BuyerQueueProjectionReuseAndReturnKindTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly DateTime Today = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

    private static Proj.ItemInput Item(string? lifecycle = null) => new(Guid.NewGuid(), false, lifecycle, null, false);

    private static Proj.RequestInput Req(
        IEnumerable<Proj.ItemInput> items, IEnumerable<Proj.BatchInput>? batches = null,
        IEnumerable<Proj.QuotationItemInput>? quotationItems = null, string status = "PO_PARTIALLY_UPLOADED",
        bool hasPoCorrection = false, IReadOnlyList<string?>? kinds = null, bool registrationPending = false, string type = "QUOTATION")
        => new(Guid.NewGuid(), "REQ-28/10/2026-628", "TEST A", type, status, false, Me, null, null, Today.AddDays(-3), false, false,
            items.ToList(), (batches ?? Array.Empty<Proj.BatchInput>()).ToList(), (quotationItems ?? Array.Empty<Proj.QuotationItemInput>()).ToList(),
            Array.Empty<Guid>(), hasPoCorrection, kinds, registrationPending);

    /// <summary>REQ-628 after "voltar a cotar" of group S1: line 1 released (its TREVOTECH candidate now reuse-blocked),
    /// line 2 approved and held by the same APPROVED batch (group S2 PO_ISSUED).</summary>
    private static (Proj.RequestInput req, Proj.ItemInput released, Proj.ItemInput approved, Guid oldCandidate) Req628(bool oldCandidateBlocked, bool addNewQuotation = false)
    {
        var released = Item("QUOTATION_PENDING");
        var approved = Item("QUOTATION_APPROVED");
        var oldCandidate = Guid.NewGuid();
        var batch = new Proj.BatchInput(Guid.NewGuid(), 1, "APPROVED", new[]
        {
            new Proj.BatchItemInput(released.Id, oldCandidate, new[] { oldCandidate }, IsReleased: true),
            new Proj.BatchItemInput(approved.Id, Guid.NewGuid(), Array.Empty<Guid>()),
        });
        var qis = new List<Proj.QuotationItemInput> { new(oldCandidate, released.Id, "MAPPED", IsReuseBlocked: oldCandidateBlocked) };
        if (addNewQuotation) qis.Add(new(Guid.NewGuid(), released.Id, "MAPPED"));
        return (Req(new[] { released, approved }, new[] { batch }, qis), released, approved, oldCandidate);
    }

    [Fact]
    public void Released_line_with_only_a_reuse_blocked_candidate_is_pending_and_keeps_quotation_entries()
    {
        var (req, released, approved, _) = Req628(oldCandidateBlocked: true);
        var p = Proj.Build(req, Me, Today);
        var buckets = Proj.ClassifyItemCoverage(req);

        Assert.Equal(S.CoverageBuckets.PendingQuotation, buckets[released.Id]);   // not "Pronto para lote"
        Assert.Equal(S.CoverageBuckets.Approved, buckets[approved.Id]);
        Assert.Equal(1, p.PendingCount);
        Assert.Equal(0, p.CoverageCounts[S.CoverageBuckets.QuotedReadyForBatch]);
        Assert.Equal(S.OperationalStates.PartialCoverage, p.OperationalState);   // not READY_FOR_APPROVAL
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.AddQuotation && a.Actionable);
        Assert.DoesNotContain(p.NextBuyerActions, a => a.Code == S.ActionCodes.SubmitBatch); // nothing eligible to submit
    }

    [Fact]
    public void Authorized_reuse_or_a_new_quotation_makes_the_released_line_ready()
    {
        var (authorized, released, _, _) = Req628(oldCandidateBlocked: false); // IsReuseBlocked=false ⇔ active authorization
        Assert.Equal(S.CoverageBuckets.QuotedReadyForBatch, Proj.ClassifyItemCoverage(authorized)[released.Id]);
        var p1 = Proj.Build(authorized, Me, Today);
        Assert.Equal(S.OperationalStates.ReadyForApproval, p1.OperationalState);
        Assert.Contains(p1.NextBuyerActions, a => a.Code == S.ActionCodes.SubmitBatch && a.Actionable);

        var (withNew, released2, _, _) = Req628(oldCandidateBlocked: true, addNewQuotation: true);
        Assert.Equal(S.CoverageBuckets.QuotedReadyForBatch, Proj.ClassifyItemCoverage(withNew)[released2.Id]);
        Assert.Equal(S.OperationalStates.ReadyForApproval, Proj.Build(withNew, Me, Today).OperationalState);
    }

    [Fact]
    public void Reuse_blocked_candidate_is_ignored_even_on_an_active_quotation_scalar()
    {
        var line = Item(null);
        var blocked = new Proj.QuotationItemInput(Guid.NewGuid(), line.Id, "MAPPED", IsReuseBlocked: true);
        var cancelledBatch = new Proj.BatchInput(Guid.NewGuid(), 1, "CANCELLED", new[] { new Proj.BatchItemInput(line.Id, blocked.Id, new[] { blocked.Id }) });
        var p = Proj.Build(Req(new[] { line }, new[] { cancelledBatch }, new[] { blocked }, status: "WAITING_QUOTATION"), Me, Today);
        Assert.Equal(S.OperationalStates.NeedsQuotation, p.OperationalState);
        Assert.Equal(1, p.PendingCount);
    }

    // ── PO_CORRECTION wording by stored return kind ──

    [Fact]
    public void PoCorrection_correction_kind_keeps_the_historical_wording_and_order()
    {
        var p = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, hasPoCorrection: true, kinds: new string?[] { K.PoCorrection }), Me, Today);
        Assert.Equal(S.OperationalStates.PoCorrection, p.OperationalState);
        Assert.Equal("Correção de P.O.", p.OperationalStateLabel);
        Assert.Equal(S.ActionCodes.CorrectPo, p.NextBuyerActions[0].Code);
        Assert.Equal("Corrigir P.O.", p.NextBuyerActions[0].Label);
        Assert.Equal(S.ActionCodes.CancelPoGroup, p.NextBuyerActions[1].Code);
    }

    [Fact]
    public void PoCorrection_cancellation_requested_leads_with_the_cancellation_and_names_the_advisory()
    {
        var p = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, hasPoCorrection: true, kinds: new string?[] { K.OrderCancellationRequested }), Me, Today);
        Assert.Equal("P.O. devolvida — cancelamento solicitado", p.OperationalStateLabel);
        Assert.Equal(S.ActionCodes.CancelPoGroup, p.NextBuyerActions[0].Code);
        Assert.Contains("cancelamento solicitado", p.NextBuyerActions[0].Label);
        // The correction capability is NOT removed (the kind is advisory).
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.CorrectPo && a.Actionable);
        Assert.True(p.RequiresAttention);
    }

    [Fact]
    public void PoCorrection_legacy_unclassified_and_mixed_stay_neutral()
    {
        var legacy = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, hasPoCorrection: true, kinds: new string?[] { null }), Me, Today);
        Assert.Equal("P.O. devolvida por Finanças", legacy.OperationalStateLabel);
        Assert.Equal("Rever a P.O. devolvida (corrigir ou cancelar)", legacy.NextBuyerActions[0].Label);
        Assert.Contains(legacy.NextBuyerActions, a => a.Code == S.ActionCodes.CancelPoGroup);

        var mixed = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, hasPoCorrection: true, kinds: new string?[] { K.PoCorrection, K.OrderCancellationRequested }), Me, Today);
        Assert.Equal("P.O. devolvidas por Finanças", mixed.OperationalStateLabel);

        // No kinds at all (caller did not supply them) → neutral, never "correction" by default.
        var none = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, hasPoCorrection: true), Me, Today);
        Assert.Equal("P.O. devolvida por Finanças", none.OperationalStateLabel);
    }

    // ── first P.O. registration pending ──

    [Fact]
    public void Group_awaiting_first_po_is_PoRegistrationPending_informational_and_hidden_by_default()
    {
        var p = Proj.Build(Req(new[] { Item("QUOTATION_APPROVED") }, status: "FINAL_APPROVED", registrationPending: true), Me, Today);
        Assert.Equal(S.OperationalStates.PoRegistrationPending, p.OperationalState);
        Assert.Equal("P.O. a registar", p.OperationalStateLabel);
        var a = Assert.Single(p.NextBuyerActions);
        Assert.Equal(S.ActionCodes.RegisterPo, a.Code);
        Assert.False(a.Actionable); // never personal-action / alert / workload material
        Assert.Contains(S.OperationalStates.PoRegistrationPending, S.OperationalStates.HiddenByDefault);
        Assert.False(p.RequiresAttention);
        Assert.Equal(S.PriorityBands.Standard, p.PriorityBand);
    }

    [Fact]
    public void Registration_pending_never_overrides_open_quotation_work_correction_or_terminal()
    {
        var released = Item("QUOTATION_PENDING"); var approved = Item("QUOTATION_APPROVED");
        var batch = new Proj.BatchInput(Guid.NewGuid(), 1, "APPROVED", new[]
        {
            new Proj.BatchItemInput(released.Id, null, Array.Empty<Guid>(), IsReleased: true),
            new Proj.BatchItemInput(approved.Id, Guid.NewGuid(), Array.Empty<Guid>()),
        });
        var open = Proj.Build(Req(new[] { released, approved }, new[] { batch }, registrationPending: true), Me, Today);
        Assert.Equal(S.OperationalStates.PartialCoverage, open.OperationalState);

        var correction = Proj.Build(Req(new[] { approved }, hasPoCorrection: true, kinds: new string?[] { K.PoCorrection }, registrationPending: true), Me, Today);
        Assert.Equal(S.OperationalStates.PoCorrection, correction.OperationalState);

        var cancelled = Proj.Build(Req(new[] { approved }, status: "CANCELLED", registrationPending: true), Me, Today);
        Assert.Equal(S.OperationalStates.CompletedForBuyer, cancelled.OperationalState);

        var payment = Proj.Build(Req(new[] { approved }, registrationPending: true, type: "PAYMENT"), Me, Today);
        Assert.Equal(S.OperationalStates.NoBuyerAction, payment.OperationalState);
    }
}
