using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.245.3 — Request Details guidance uses group/unit truth. A single operational group whose items are
/// ALL received but which is not yet confirmed must guide to "Recebimento completo — confirmar recebimento",
/// never the stale scalar "Resolver itens pendentes…". Partial stays pending; post-confirmation asks for the
/// supplier receipt. Pure builder unit tests (no DB).
/// </summary>
public class RequestWorkflowProjectionReceivingGuidanceTests
{
    private static Request BuildSingleGroupPaymentRequest(string groupStatus, params string[] itemStatusCodes)
    {
        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var pending = new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" };

        var reqId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var group = new RequestPoGroup { Id = groupId, RequestId = reqId, Status = groupStatus, SupplierNameSnapshot = "Fornecedor" };

        var req = new Request
        {
            Id = reqId, RequestNumber = "ZZTEST-PROJ", Title = "proj",
            RequestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" }, RequestTypeId = 2,
            Status = new RequestStatus { Id = 18, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento" }, StatusId = 18,
        };

        var ln = 1;
        foreach (var code in itemStatusCodes)
        {
            var st = code == "RECEIVED" ? received : pending;
            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = reqId, RequestPoGroupId = groupId, LineNumber = ln++,
                Quantity = 1, ReceivedQuantity = code == "RECEIVED" ? 1 : 0,
                LineItemStatus = st, LineItemStatusId = st.Id, IsDeleted = false
            };
            req.LineItems.Add(li);
            group.LineItems.Add(li);
        }
        req.PoGroups.Add(group);
        return req;
    }

    [Fact]
    public void PaymentCompleted_group_allReceived_guides_to_confirm_receiving()
    {
        var req = BuildSingleGroupPaymentRequest(RequestConstants.PoGroupStatuses.PaymentCompleted, "RECEIVED", "RECEIVED");
        var proj = RequestWorkflowProjectionBuilder.Build(req, req.Status!.Code);
        var unit = Assert.Single(proj.Units);
        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
        Assert.Equal("Recebimento completo — confirmar recebimento", unit.NextAction.Label);
        Assert.DoesNotContain("pendentes", unit.NextAction.Label);
        Assert.Equal("Recebimento", unit.ResponsibleRole);
    }

    [Fact]
    public void PaymentCompleted_group_partial_stays_pending_not_confirm()
    {
        var req = BuildSingleGroupPaymentRequest(RequestConstants.PoGroupStatuses.PaymentCompleted, "RECEIVED", "PENDING");
        var proj = RequestWorkflowProjectionBuilder.Build(req, req.Status!.Code);
        var unit = Assert.Single(proj.Units);
        Assert.NotEqual("CONFIRM_RECEIVING", unit.NextAction!.ActionType); // not yet confirmable — receiving in progress
        Assert.Contains("conferir itens", unit.NextAction.Label);
    }

    [Fact]
    public void InFollowup_group_allReceived_guides_to_confirm_receiving()
    {
        var req = BuildSingleGroupPaymentRequest(RequestConstants.PoGroupStatuses.InFollowup, "RECEIVED");
        var proj = RequestWorkflowProjectionBuilder.Build(req, req.Status!.Code);
        var unit = Assert.Single(proj.Units);
        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
        Assert.Equal("Recebimento completo — confirmar recebimento", unit.NextAction.Label);
    }

    [Fact]
    public void InFollowup_group_partial_indicates_pending_items()
    {
        var req = BuildSingleGroupPaymentRequest(RequestConstants.PoGroupStatuses.InFollowup, "RECEIVED", "PENDING");
        var proj = RequestWorkflowProjectionBuilder.Build(req, req.Status!.Code);
        var unit = Assert.Single(proj.Units);
        Assert.Equal("RESOLVE_FOLLOWUP", unit.NextAction!.ActionType);
        Assert.Contains("pendentes", unit.NextAction.Label);
    }

    [Fact]
    public void WaitingReceipt_group_guides_to_attach_supplier_receipt_and_finalize()
    {
        var req = BuildSingleGroupPaymentRequest(RequestConstants.PoGroupStatuses.WaitingReceipt, "RECEIVED");
        var proj = RequestWorkflowProjectionBuilder.Build(req, req.Status!.Code);
        var unit = Assert.Single(proj.Units);
        Assert.Equal("ATTACH_RECEIPT", unit.NextAction!.ActionType);
        Assert.Equal("Anexar recibo do fornecedor e finalizar pedido", unit.NextAction.Label);
    }
}
