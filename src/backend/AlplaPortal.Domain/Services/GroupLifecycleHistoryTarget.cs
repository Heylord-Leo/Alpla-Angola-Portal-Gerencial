using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.245.9 — the DISPLAYED target of a group-scoped lifecycle audit row.
///
/// <para><b>Why this exists.</b> <c>RequestStatusHistory.NewStatusId</c> is a non-nullable foreign
/// key to <c>RequestStatuses</c> — the REQUEST status domain. Group statuses
/// (<see cref="RequestConstants.PoGroupStatuses"/>) are a different domain with no table, so a
/// group-scoped event cannot persist "the group became COMPLETED" in that column. The writer
/// (<c>RequestCompletionService.AddHistoryOnceAsync</c>) therefore carries the parent request's
/// scalar unchanged (Previous = New = the scalar), which is truthful for the request — and the
/// request-level readers that reconstruct transitions from <c>NewStatus.Code</c> (completion
/// timeline, Finance monthly counts, stage detection) depend on exactly that: persisting the
/// request's COMPLETED status on a GROUP_COMPLETED row would declare a request completed the moment
/// its FIRST group completed.</para>
///
/// <para><b>What it does.</b> Resolves, from the event code alone, the GROUP status the event
/// produced — so the history DTO can present "→ Concluído" for GROUP_COMPLETED whatever the request
/// scalar happened to be (WAITING_RECEIPT, PAYMENT_COMPLETED, …), for the only group, for one of
/// several, and when the request completes in the same operation. REQUEST_COMPLETED remains the
/// request-level transition and keeps the persisted status name ("Finalizado"). Every other event
/// passes through unchanged, so existing rows stay exactly as readable as before. Pure, read-side,
/// no schema or data change — already-persisted rows render correctly without repair.</para>
/// </summary>
public static class GroupLifecycleHistoryTarget
{
    /// <summary>
    /// The group status an event code transitions its group INTO, or null when the event is not a
    /// group status transition (request-level transitions, stamps, documents, notes …).
    /// </summary>
    public static string? ResolveGroupStatusCode(string? actionTaken) => actionTaken switch
    {
        WorkflowEventCodes.GroupCompleted => RequestConstants.PoGroupStatuses.Completed,
        WorkflowEventCodes.FiscalReceiptUnlocked => RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
        _ => null
    };

    /// <summary>Display name of the group statuses the resolver can produce.</summary>
    public static string GroupStatusDisplayName(string groupStatusCode) => groupStatusCode switch
    {
        RequestConstants.PoGroupStatuses.Completed => "Concluído",
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt => "Aguardando Recibo Fiscal",
        _ => groupStatusCode
    };

    /// <summary>
    /// The name a history row should DISPLAY as its resulting state: the group's resulting status
    /// for a group-scoped lifecycle transition, else the persisted request status name unchanged.
    /// </summary>
    public static string ResolveDisplayName(string? actionTaken, string requestStatusName)
    {
        var groupStatus = ResolveGroupStatusCode(actionTaken);
        return groupStatus == null ? requestStatusName : GroupStatusDisplayName(groupStatus);
    }
}
