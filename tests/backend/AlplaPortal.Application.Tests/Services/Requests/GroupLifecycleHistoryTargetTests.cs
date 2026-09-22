using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.245.9 — the read-side target of group-scoped lifecycle audit rows. The status FKs of those
/// rows carry the REQUEST scalar (a different status domain); what the user must see is the GROUP's
/// resulting state. Everything that is not a group status transition passes through unchanged.
/// </summary>
public class GroupLifecycleHistoryTargetTests
{
    [Fact]
    public void Group_completed_targets_the_group_completed_state_whatever_the_request_scalar_was()
    {
        Assert.Equal("Concluído", GroupLifecycleHistoryTarget.ResolveDisplayName(WorkflowEventCodes.GroupCompleted, "Aguardando Recibo"));
        Assert.Equal("Concluído", GroupLifecycleHistoryTarget.ResolveDisplayName(WorkflowEventCodes.GroupCompleted, "Pagamento Realizado"));
        Assert.Equal("Concluído", GroupLifecycleHistoryTarget.ResolveDisplayName(WorkflowEventCodes.GroupCompleted, "Em Acompanhamento"));
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, GroupLifecycleHistoryTarget.ResolveGroupStatusCode(WorkflowEventCodes.GroupCompleted));
    }

    [Fact]
    public void Fiscal_receipt_unlocked_targets_the_group_antechamber_state()
    {
        Assert.Equal("Aguardando Recibo Fiscal", GroupLifecycleHistoryTarget.ResolveDisplayName(WorkflowEventCodes.FiscalReceiptUnlocked, "Aguardando Recibo"));
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, GroupLifecycleHistoryTarget.ResolveGroupStatusCode(WorkflowEventCodes.FiscalReceiptUnlocked));
    }

    [Fact]
    public void Request_completed_keeps_the_persisted_request_status_name()
    {
        // The request-level transition stays authoritative: "Finalizado", never relabelled.
        Assert.Equal("Finalizado", GroupLifecycleHistoryTarget.ResolveDisplayName("REQUEST_COMPLETED", "Finalizado"));
        Assert.Null(GroupLifecycleHistoryTarget.ResolveGroupStatusCode("REQUEST_COMPLETED"));
    }

    [Theory]
    [InlineData("STATUS_SYNC")]
    [InlineData("SUBMIT")]
    [InlineData("CONFIRM_RECEIVING")]
    [InlineData("RECEIVING_REOPENED")]
    [InlineData("OPERATIONAL_RECEIPT_COMPLETED")]
    [InlineData("FISCAL_RECEIPT_UPLOADED")]
    [InlineData("GRUPO_CLASSIFICADO")]
    [InlineData("DOCUMENTO ADICIONADO")]
    [InlineData("")]
    [InlineData(null)]
    public void Every_other_event_passes_its_persisted_status_name_through_unchanged(string? actionTaken)
    {
        Assert.Equal("Em Acompanhamento", GroupLifecycleHistoryTarget.ResolveDisplayName(actionTaken, "Em Acompanhamento"));
        Assert.Null(GroupLifecycleHistoryTarget.ResolveGroupStatusCode(actionTaken));
    }

    [Fact]
    public void Group_status_display_names_are_the_established_labels()
    {
        Assert.Equal("Concluído", GroupLifecycleHistoryTarget.GroupStatusDisplayName(RequestConstants.PoGroupStatuses.Completed));
        Assert.Equal("Aguardando Recibo Fiscal", GroupLifecycleHistoryTarget.GroupStatusDisplayName(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt));
        Assert.Equal("WAITING_PO", GroupLifecycleHistoryTarget.GroupStatusDisplayName("WAITING_PO"));   // never invents a label
    }
}
