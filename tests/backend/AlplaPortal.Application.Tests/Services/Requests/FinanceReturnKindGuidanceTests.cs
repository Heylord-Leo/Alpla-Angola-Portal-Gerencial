using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.Services.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;
using K = AlplaPortal.Domain.Constants.PoGroupCancellationConstants.FinanceReturnKinds;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.2 — the STORED Finance return kind (RequestPoGroup.LastFinanceReturnKind) drives every current guidance:
/// the Finance obligations row, the request-page / print "Próxima ação" (RequestWorkflowProjectionBuilder) and the
/// Para Minha Ação card (label + a route that leads to the place where the Buyer acts). Correction keeps the
/// historical wording; cancellation-requested names the advisory; legacy (null) stays neutral — nothing is inferred
/// from the reason text, and no capability is removed.
/// </summary>
public class FinanceReturnKindGuidanceTests
{
    private static readonly DateTime Today = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

    // ── Finance obligations row ──

    private static FinanceObligationProjectionBuilder.GroupInput ReturnedGroup(string? kind) =>
        new(Guid.NewGuid(), 5, "TREVOTECH LDA", "5000000000", "5000000000", RequestConstants.PoGroupStatuses.WaitingPoCorrection,
            "ECF99 2026/9001", "AOA", 250_000m, Array.Empty<string>(), Array.Empty<FinanceObligationProjectionBuilder.PaymentInput>(), kind);

    [Theory]
    [InlineData(K.PoCorrection, "Devolvido para correção da P.O.")]
    [InlineData(K.OrderCancellationRequested, "Devolvido — cancelamento solicitado (não corrigir)")]
    [InlineData(null, "Devolvido para Compras (tipo não classificado)")]
    public void Finance_row_next_action_names_the_stored_kind(string? kind, string expected)
    {
        var req = new FinanceObligationProjectionBuilder.RequestInput(Guid.NewGuid(), "REQ-28/10/2026-628", RequestConstants.Types.Quotation, "t", "Manutenção", "Viana 3");
        var o = FinanceObligationProjectionBuilder.Build(req, ReturnedGroup(kind), Today);
        Assert.Equal(FinanceActionClasses.NoFinanceAction, o.ActionClass);
        Assert.Equal(expected, o.NextActionLabel);
        Assert.Equal("Devolvido para Compras", o.GroupStatusLabel); // the status label itself stays neutral
    }

    // ── request page / print "Próxima ação" ──

    private static Request RequestWithReturnedGroup(string? kind)
    {
        var r = new Request
        {
            Id = Guid.NewGuid(), Title = "t",
            Status = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.WaitingPoCorrection },
            RequestType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
        };
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, SupplierNameSnapshot = "TREVOTECH LDA", PurchaseOrderNumber = "ECF99 2026/9001", TotalAmount = 250_000m, CurrencyCode = "AOA", LastFinanceReturnKind = kind });
        return r;
    }

    [Theory]
    [InlineData(K.PoCorrection, "Corrigir P.O devolvida por Finanças e re-registrar")]
    [InlineData(K.OrderCancellationRequested, "P.O devolvida por Finanças — cancelamento solicitado: cancelar a P.O (voltar a cotar ou encerrar itens) ou, se discutível, corrigir")]
    [InlineData(null, "Rever a P.O devolvida por Finanças (corrigir ou cancelar)")]
    public void Workflow_projection_next_action_follows_the_stored_kind_and_keeps_CORRECT_PO(string? kind, string expected)
    {
        var projection = RequestWorkflowProjectionBuilder.Build(RequestWithReturnedGroup(kind), RequestConstants.Statuses.WaitingPoCorrection);
        var unit = Assert.Single(projection.Units);
        Assert.NotNull(unit.NextAction);
        Assert.Equal("CORRECT_PO", unit.NextAction!.ActionType); // capability unchanged; the kind is advisory
        Assert.Equal(expected, unit.NextAction.Label);
        Assert.Equal("Comprador", unit.NextAction.ResponsibleRole);
    }

    // ── Para Minha Ação card ──

    private static Request PersonalRequest(Guid buyer, string typeCode, params string?[] kinds)
    {
        var r = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-28/10/2026-628", Title = "t", BuyerId = typeCode == RequestConstants.Types.Quotation ? buyer : null,
            Status = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.WaitingPoCorrection },
            RequestType = new RequestType { Id = 1, Code = typeCode, Name = typeCode },
            CreatedAtUtc = Today,
        };
        foreach (var kind in kinds)
            r.PoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, SupplierNameSnapshot = "S",
                PurchaseOrderNumber = "ECF99 2026/9001", TotalAmount = 1m, CurrencyCode = "AOA", LastFinanceReturnKind = kind,
                PoResponsibleBuyerId = typeCode == RequestConstants.Types.Quotation ? null : buyer,
            });
        return r;
    }

    private static PersonalActionProjectionService.Context Buyer(Guid id) => new(id, IsBuyer: true, IsFinance: false, IsReceiver: false, IsFinalApprover: false, Today);

    [Theory]
    [InlineData(K.PoCorrection, "Corrigir P.O.")]
    [InlineData(K.OrderCancellationRequested, "P.O. devolvida — cancelamento solicitado")]
    [InlineData(null, "Rever P.O. devolvida")]
    public void Personal_action_label_follows_the_stored_kind_and_routes_a_QUOTATION_request_to_its_Workspace_groups_tab(string? kind, string expectedLabel)
    {
        var buyer = Guid.NewGuid();
        var r = PersonalRequest(buyer, RequestConstants.Types.Quotation, kind);
        var res = new PersonalActionProjectionService().Build(new[] { r }, Buyer(buyer), null, 1, 20, null, null, null, null);
        var item = Assert.Single(res.Items, i => i.ActionType == PersonalActionPredicates.ActionTypes.PoCorrection);
        Assert.Equal(expectedLabel, item.ActionLabel);
        Assert.Equal($"/buyer/requests/{r.Id}?tab=groups", item.Route);
        Assert.Equal(kind, Assert.Single(item.AffectedSuppliers).ReturnKind);
    }

    [Fact]
    public void Personal_action_for_a_PAYMENT_correction_keeps_the_deep_link_and_mixed_kinds_stay_neutral()
    {
        var buyer = Guid.NewGuid();
        var r = PersonalRequest(buyer, RequestConstants.Types.Payment, K.PoCorrection, K.OrderCancellationRequested);
        var res = new PersonalActionProjectionService().Build(new[] { r }, Buyer(buyer), null, 1, 20, null, null, null, null);
        var item = Assert.Single(res.Items, i => i.ActionType == PersonalActionPredicates.ActionTypes.PoCorrection);
        Assert.Equal("Rever P.O. devolvida", item.ActionLabel);
        // §G.20 contract unchanged for PAYMENT: Para Minha Ação deep-link with the group id (card targeting + single-group claim).
        Assert.Contains("action=PO_CORRECTION", item.Route);
        Assert.Contains($"requestId={r.Id}", item.Route);
        Assert.Contains("poGroupId=", item.Route);
        Assert.Equal(2, item.AffectedSuppliers.Count);
    }
}
