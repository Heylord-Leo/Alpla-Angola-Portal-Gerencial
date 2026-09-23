using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.230.0 — Multi-Group Request Workflow projection. Acceptance scenarios from the release
/// spec: single-unit equivalence (A), mixed states (C/D), superseded batch (G), terminal (H)
/// and the REQ-140 repaired shape (I).
/// </summary>
public class RequestWorkflowProjectionBuilderTests
{
    private static Request MakeRequest(string statusCode, string typeCode = RequestConstants.Types.Quotation) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Status = new RequestStatus { Id = 1, Code = statusCode },
            RequestType = new RequestType { Id = 1, Code = typeCode, Name = typeCode }
        };

    private static RequestPoGroup Group(string status, string? supplier = "FORNECEDOR X", string? po = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            SupplierNameSnapshot = supplier,
            PurchaseOrderNumber = po,
            TotalAmount = 100m,
            CurrencyCode = "AOA"
        };

    [Fact]
    public void CaseA_SingleGroupWaitingPo_SingleUnit_SameLegacyGuidanceStrings()
    {
        var request = MakeRequest("APPROVED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo));

        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("GROUP", unit.UnitType);
        // Compatibility rule: identical strings to lib/utils.ts getRequestGuidance('APPROVED').
        Assert.Equal("Comprador", unit.ResponsibleRole);
        Assert.Equal("Prosseguir com a emissão ou inserção da P.O", unit.NextAction!.Label);
        Assert.Empty(projection.Warnings);
    }

    [Fact]
    public void CaseC_IssuedGroup_PlusLiveBatchInFinalApproval_BothActionsVisible()
    {
        var request = MakeRequest("PO_ISSUED");
        var issued = Group(RequestConstants.PoGroupStatuses.PoIssued, po: "ECF10 2026/1");
        request.PoGroups.Add(issued);

        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING");

        Assert.Equal(2, projection.Units.Count);
        Assert.Equal("Processamento Parcial", projection.AggregateDisplay.Label);
        Assert.Equal(2, projection.NextActions.Count);
        // The approval wave unblocks first (lower priority number), the issued group's
        // finance action follows.
        Assert.Equal("FINAL_APPROVE", projection.NextActions[0].ActionType);
        Assert.Equal("SCHEDULE_PAYMENT", projection.NextActions[1].ActionType);
        Assert.Contains(projection.Responsibilities, r => r.Role == "Aprovador Final");
        Assert.Contains(projection.Responsibilities, r => r.Role == "Financeiro");
    }

    [Fact]
    public void CaseD_PaymentScheduled_PlusWaitingPo_MixedRolesAndActions()
    {
        var request = MakeRequest("PO_PARTIALLY_UPLOADED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PaymentScheduled, supplier: "A"));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, supplier: "B"));

        var projection = RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING");

        Assert.Equal(2, projection.Units.Count);
        Assert.Equal("REGISTER_PO", projection.NextActions[0].ActionType); // furthest behind first
        Assert.Contains(projection.Responsibilities, r => r.Role == "Comprador");
        Assert.Contains(projection.Responsibilities, r => r.Role == "Financeiro");
    }

    [Fact]
    public void CaseG_SupersededBatch_NeverAnActiveUnit_SurfacesAsWarning()
    {
        var request = MakeRequest("PO_ISSUED");
        var group = Group(RequestConstants.PoGroupStatuses.PoIssued, supplier: "PAPELARIA CASA DO PAPEL", po: "5001334395");
        request.PoGroups.Add(group);

        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestPoGroupId = group.Id };
        request.LineItems.Add(li);
        var stale = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment };
        stale.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = stale.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(stale);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_ISSUED");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("GROUP", unit.UnitType);
        Assert.DoesNotContain(projection.Responsibilities, r => r.Role == "Comprador");
        Assert.DoesNotContain(projection.NextActions, a => a.UnitType == "BATCH");
        var warning = Assert.Single(projection.Warnings);
        Assert.Equal("Lote #1 obsoleto — os itens deste lote já foram processados por outro fluxo.", warning);
    }

    // ── v2.245.11: PAYMENT-type advance lifecycle guidance (authoritative for the details header) ──

    [Fact]
    public void V224511_PaymentType_AdvanceScheduled_SingleGroup_FinanceConfirmAdvance_NoDueDateWording()
    {
        var request = MakeRequest("ADVANCE_PAYMENT_SCHEDULED", RequestConstants.Types.Payment);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.AdvancePaymentScheduled, po: "ECF11 2026/520"));

        var projection = RequestWorkflowProjectionBuilder.Build(request, "ADVANCE_PAYMENT_SCHEDULED");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("GROUP", unit.UnitType);
        Assert.Equal("ADVANCE_PAYMENT_SCHEDULED", unit.StatusCode);
        Assert.Equal("Financeiro", unit.ResponsibleRole);
        Assert.NotNull(unit.NextAction);
        Assert.Equal("CONFIRM_ADVANCE", unit.NextAction!.ActionType);
        Assert.Equal("Confirmar o pagamento do adiantamento", unit.NextAction.Label);
        var action = Assert.Single(projection.NextActions);
        Assert.Equal("Financeiro", action.ResponsibleRole);
        Assert.Equal("Confirmar o pagamento do adiantamento", action.Label);
        // No due-date / deadline wording anywhere in the guidance strings.
        Assert.DoesNotMatch("(?i)venc|prazo|data", action.Label);
        Assert.DoesNotMatch("(?i)venc|prazo|data", action.ResponsibleRole);
        // Never the generic placeholders the frontend used to fall back to.
        Assert.NotEqual("Não definido", unit.ResponsibleRole);
        Assert.NotEqual("Aguardar atualização do sistema", action.Label);
    }

    [Theory]
    [InlineData("ADVANCE_PAYMENT_REQUIRED", "Financeiro", "SCHEDULE_ADVANCE", "Agendar o adiantamento")]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED", "Financeiro", "CONFIRM_ADVANCE", "Confirmar o pagamento do adiantamento")]
    [InlineData("PAYMENT_SCHEDULED", "Financeiro", "COMPLETE_PAYMENT", "Realizar o pagamento")]
    public void V224511_PaymentType_ActionablePaymentStates_FinanceOwnsTheNextAction(string status, string role, string actionType, string label)
    {
        var request = MakeRequest(status, RequestConstants.Types.Payment);
        request.PoGroups.Add(Group(status));

        var projection = RequestWorkflowProjectionBuilder.Build(request, status);

        var unit = Assert.Single(projection.Units);
        Assert.Equal(role, unit.ResponsibleRole);
        Assert.Equal(actionType, unit.NextAction!.ActionType);
        Assert.Equal(label, unit.NextAction.Label);
    }

    [Fact]
    public void V224511_PaymentType_TwoAdvanceScheduledGroups_TwoGroupSpecificActions_NoDuplicates()
    {
        // REQ-27/08/2026-333 shape: one PAYMENT request, two supplier groups each with a scheduled advance.
        var request = MakeRequest("ADVANCE_PAYMENT_SCHEDULED", RequestConstants.Types.Payment);
        var a = Group(RequestConstants.PoGroupStatuses.AdvancePaymentScheduled, supplier: "FORNECEDOR A");
        var b = Group(RequestConstants.PoGroupStatuses.AdvancePaymentScheduled, supplier: "FORNECEDOR B");
        request.PoGroups.Add(a);
        request.PoGroups.Add(b);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "ADVANCE_PAYMENT_SCHEDULED");

        Assert.Equal(2, projection.Units.Count);
        Assert.Equal(2, projection.NextActions.Count);
        Assert.Equal(2, projection.NextActions.Select(x => x.UnitId).Distinct().Count());
        Assert.All(projection.NextActions, x =>
        {
            Assert.Equal("Financeiro", x.ResponsibleRole);
            Assert.Equal("CONFIRM_ADVANCE", x.ActionType);
            Assert.Equal("Confirmar o pagamento do adiantamento", x.Label);
        });
        Assert.Contains(projection.NextActions, x => x.UnitId == a.Id && x.UnitLabel == "Grupo FORNECEDOR A");
        Assert.Contains(projection.NextActions, x => x.UnitId == b.Id && x.UnitLabel == "Grupo FORNECEDOR B");
        var finance = Assert.Single(projection.Responsibilities, r => r.Role == "Financeiro");
        Assert.Equal(2, finance.UnitCount);
    }

    [Fact]
    public void V224511_PaymentType_ConfirmedAdvance_NoLongerAsksFinance()
    {
        // b2p/confirm-advance hands the group to WAITING_SUPPLIER_DELIVERY — the Buyer follows the delivery.
        var request = MakeRequest("WAITING_SUPPLIER_DELIVERY", RequestConstants.Types.Payment);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingSupplierDelivery));

        var projection = RequestWorkflowProjectionBuilder.Build(request, "WAITING_SUPPLIER_DELIVERY");

        var unit = Assert.Single(projection.Units);
        Assert.NotEqual("Financeiro", unit.ResponsibleRole);
        Assert.NotEqual("CONFIRM_ADVANCE", unit.NextAction!.ActionType);
        Assert.DoesNotContain(projection.Responsibilities, r => r.Role == "Financeiro");
    }

    /// <summary>Acceptance case I — REQ-140 after repair: PO issued, Finance next, never Buyer/register-PO.</summary>
    [Fact]
    public void CaseI_Req140RepairedShape_FinanceNext_NeverBuyerRegisterPo()
    {
        var request = MakeRequest("PO_ISSUED");
        var group = Group(RequestConstants.PoGroupStatuses.PoIssued, supplier: "PAPELARIA CASA DO PAPEL", po: "ECF10 2026/251");
        request.PoGroups.Add(group);

        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestPoGroupId = group.Id };
        request.LineItems.Add(li);
        var stale = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment };
        stale.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = stale.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(stale);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_ISSUED");

        Assert.Equal("P.O Emitida", projection.AggregateDisplay.Label);
        var action = Assert.Single(projection.NextActions);
        Assert.Equal("Financeiro", action.ResponsibleRole);
        Assert.Equal("SCHEDULE_PAYMENT", action.ActionType);
        Assert.DoesNotContain(projection.NextActions, a => a.ActionType == "REGISTER_PO");
        Assert.Equal("Grupo PAPELARIA CASA DO PAPEL", action.UnitLabel);
    }

    /// <summary>
    /// Fix 1 (scenario A of the fix spec): a PENDING group tied to a batch that is itself an
    /// active BATCH unit is only that wave's pre-activation representation — exactly ONE unit
    /// (the batch), no duplicated responsibilities or actions.
    /// </summary>
    [Fact]
    public void Fix1A_ActiveBatch_PlusItsOwnPendingGroup_EmitsSingleBatchUnit()
    {
        var request = MakeRequest("PO_ISSUED");
        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);

        var pendingGroup = Group(RequestConstants.PoGroupStatuses.Pending, supplier: "B");
        pendingGroup.ApprovalBatchId = batch.Id;
        request.PoGroups.Add(pendingGroup);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("BATCH", unit.UnitType);
        var responsibility = Assert.Single(projection.Responsibilities);
        Assert.Equal("Aprovador Final", responsibility.Role);
        Assert.Equal(1, responsibility.UnitCount);
        Assert.Single(projection.NextActions);
    }

    /// <summary>
    /// Fix 1 (scenario B): once the batch settled, the activated group is the single active
    /// unit — never suppressed.
    /// </summary>
    [Fact]
    public void Fix1B_SettledBatch_PlusItsWaitingPoGroup_EmitsSingleGroupUnit()
    {
        var request = MakeRequest("PO_REQUESTED");
        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);

        var group = Group(RequestConstants.PoGroupStatuses.WaitingPo);
        group.ApprovalBatchId = batch.Id;
        request.PoGroups.Add(group);
        li.RequestPoGroupId = group.Id;

        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_REQUESTED");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("GROUP", unit.UnitType);
        Assert.Equal("REGISTER_PO", unit.NextAction!.ActionType);
    }

    [Fact]
    public void CaseH_CancelledRequest_NoActiveUnits_NoActions()
    {
        var request = MakeRequest("CANCELLED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued));

        var projection = RequestWorkflowProjectionBuilder.Build(request, "CANCELLED");

        Assert.Empty(projection.Units);
        Assert.Empty(projection.NextActions);
        Assert.Equal("Cancelado", projection.AggregateDisplay.Label);
    }

    [Fact]
    public void UnitSummary_NullForSingleUnit_CompactForMulti()
    {
        var single = MakeRequest("APPROVED");
        single.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo));
        Assert.Null(RequestWorkflowProjectionBuilder.BuildUnitSummary(
            RequestWorkflowProjectionBuilder.Build(single, "APPROVED")));

        var multi = MakeRequest("PO_PARTIALLY_UPLOADED");
        multi.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, supplier: "A"));
        multi.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, supplier: "B"));
        multi.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PaymentScheduled, supplier: "C"));

        var summary = RequestWorkflowProjectionBuilder.BuildUnitSummary(
            RequestWorkflowProjectionBuilder.Build(multi, "MIXED_PROCESSING"));

        Assert.NotNull(summary);
        Assert.StartsWith("3 grupos · ", summary);
        Assert.Contains("1 aguardando P.O.", summary);
        Assert.Contains("1 P.O. emitida", summary);
        Assert.Contains("1 pagamento agendado", summary);
    }
}
