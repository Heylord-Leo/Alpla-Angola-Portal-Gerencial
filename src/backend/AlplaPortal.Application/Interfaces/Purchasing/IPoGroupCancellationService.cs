using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Application.Interfaces.Purchasing;

/// <summary>Outcome of a cancellation-related operation: HTTP-like status plus a machine code; the controller maps it.</summary>
public sealed record PoGroupCancellationOperationResult(int Status, string? Code, string? Title, string? Detail, object? Payload)
{
    public bool Succeeded => Status is >= 200 and < 300;
    public static PoGroupCancellationOperationResult Ok(object payload) => new(200, null, null, null, payload);
    public static PoGroupCancellationOperationResult Fail(int status, string code, string title, string detail) => new(status, code, title, detail, null);
}

public sealed record PoGroupCancellationActor(Guid UserId, IReadOnlyList<string> Roles);

public sealed record CancelPoGroupCommand(
    Guid RequestId,
    Guid GroupId,
    PoGroupCancellationActor Actor,
    string? Kind,
    string? ReasonCode,
    string? Reason,
    string? EcfStatus,
    string? EcfReference,
    string? EcfDeclarationNote,
    Guid? EcfEvidenceAttachmentId,
    byte[]? ExpectedGroupRowVersion);

public sealed record UpdateEcfDeclarationCommand(
    Guid CancellationId,
    PoGroupCancellationActor Actor,
    string? EcfStatus,
    string? EcfReference,
    string? Note,
    Guid? EvidenceAttachmentId,
    byte[]? ExpectedRowVersion);

public sealed record FinanceReviewCommand(
    Guid CancellationId,
    PoGroupCancellationActor Actor,
    bool Confirm,
    string? Note,
    byte[]? ExpectedRowVersion);

/// <summary>
/// Phase 1 P.O. group cancellation / return to quotation after final approval. All writes are transactional and
/// emit their notification INSIDE the transaction (in-app persistence required), as the Buyer takeover does.
/// </summary>
public interface IPoGroupCancellationService
{
    Task<PoGroupCancellationOperationResult> CancelAsync(CancelPoGroupCommand cmd);
    Task<PoGroupCancellationOperationResult> UpdateEcfDeclarationAsync(UpdateEcfDeclarationCommand cmd);
    Task<PoGroupCancellationOperationResult> ResendNotificationAsync(Guid cancellationId, PoGroupCancellationActor actor);
    Task<PoGroupCancellationOperationResult> FinanceReviewAsync(FinanceReviewCommand cmd);

    /// <summary>Read model (null when the group has no cancellation). Actions are computed for the caller.</summary>
    Task<PoGroupCancellationDto?> GetAsync(Guid groupId, PoGroupCancellationActor actor);
    Task<PoGroupCancellationEligibilityDto> EvaluateEligibilityAsync(Guid groupId, PoGroupCancellationActor actor);
    Task<FinancePoCancellationListDto> ListForFinanceAsync(PoGroupCancellationActor actor, string? status, int? plantId, string? search);
    Task<LastFinanceReturnDto?> GetLastFinanceReturnAsync(Guid groupId);
}

/// <summary>
/// D4 — the replacement-P.O. block. Evaluates, for a target group (or an explicit set of line items), every prior
/// cancellation that requires Finance confirmation and is not CONFIRMED. With <paramref name="lockRows"/> the
/// matching cancellation rows are read under UPDLOCK/HOLDLOCK (SQL Server) so a concurrent Finance confirmation
/// serializes against the caller's transaction; on other providers the lock is a no-op.
/// </summary>
public interface IPriorEcfConfirmationGuard
{
    Task<PriorEcfConfirmationDto> EvaluateForGroupAsync(Guid groupId, bool lockRows);
    Task<PriorEcfConfirmationDto> EvaluateForLineItemsAsync(IReadOnlyCollection<Guid> lineItemIds, bool lockRows);
}
