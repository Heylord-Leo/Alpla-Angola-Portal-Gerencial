namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// v2.245.5 — body of REABRIR RECEBIMENTO (POST {id}/operational/groups/{groupId}/reopen-receiving).
/// The reason is mandatory and is persisted verbatim in the RECEIVING_REOPENED audit event.
/// </summary>
public class ReopenReceivingDto
{
    public string? Reason { get; set; }
}
