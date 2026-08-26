using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Phase 4B.2 — user-facing status consistency (OPTION B). Whenever a request's single operational
/// group is WAITING_PO, the display badge reads "Aguardando P.O." even though the persisted scalar is
/// left untouched (PAYMENT stays APPROVED). This pins the canonical rule the list and detail badges
/// both consume: RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride.
/// </summary>
public class PaymentPoDisplayStatusTests
{
    private static Request BuildRequest(string typeCode, string scalarStatus, params string[] groupStatuses)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-DISP",
            Status = new RequestStatus { Code = scalarStatus, Name = scalarStatus },
            RequestType = new RequestType { Code = typeCode, Name = typeCode },
            LineItems = new List<RequestLineItem>(),
            ApprovalBatches = new List<ApprovalBatch>(),
            PoGroups = new List<RequestPoGroup>()
        };
        foreach (var gs in groupStatuses)
        {
            request.PoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                Status = gs,
                SupplierNameSnapshot = "ZZTEST FORNECEDOR",
                TotalAmount = 100m
            });
        }
        return request;
    }

    [Fact]
    public void A_Payment_Approved_WithWaitingPoGroup_DisplaysAguardandoPo()
    {
        var request = BuildRequest("PAYMENT", "APPROVED", "WAITING_PO");
        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var badge = RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, "APPROVED");

        Assert.True(badge.HasValue);
        Assert.Equal("WAITING_PO", badge!.Value.Code);
        Assert.Equal("Aguardando P.O.", badge.Value.Label);
    }

    [Fact]
    public void B_Payment_Approved_WithNoGroup_HasNoOverride_StaysAprovado()
    {
        var request = BuildRequest("PAYMENT", "APPROVED");   // zero groups
        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var badge = RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, "APPROVED");

        // No operational unit → no override → the client falls back to the scalar label ("Aprovado").
        Assert.False(badge.HasValue);
    }

    [Fact]
    public void C_Quotation_PoRequested_WithWaitingPoGroup_RemainsAguardandoPo()
    {
        var request = BuildRequest("QUOTATION", "PO_REQUESTED", "WAITING_PO");
        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_REQUESTED");

        var badge = RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, "PO_REQUESTED");

        // Whether via the override or the scalar label, the QUOTATION still reads "Aguardando P.O.".
        Assert.Equal("Aguardando P.O.", badge.HasValue ? badge!.Value.Label
            : RequestWorkflowProjectionBuilder.LabelFor("PO_REQUESTED"));
    }

    [Fact]
    public void WaitingPo_And_PoRequested_ShareTheSameLabel()
    {
        Assert.Equal("Aguardando P.O.", RequestWorkflowProjectionBuilder.LabelFor("WAITING_PO"));
        Assert.Equal("Aguardando P.O.", RequestWorkflowProjectionBuilder.LabelFor("PO_REQUESTED"));
    }

    [Fact]
    public void TerminalScalar_IsNeverOverridden()
    {
        // A cancelled request keeps its terminal label even if a stale group lingers.
        var request = BuildRequest("PAYMENT", "CANCELLED", "WAITING_PO");
        var projection = RequestWorkflowProjectionBuilder.Build(request, "CANCELLED");
        var badge = RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, "CANCELLED");
        Assert.False(badge.HasValue);
    }
}
