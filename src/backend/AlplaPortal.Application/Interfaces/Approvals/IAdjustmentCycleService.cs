using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces.Approvals;

/// <summary>
/// Outcome of staging a new structured adjustment cycle. On success <see cref="Cycle"/> is the
/// entity ADDED to the DbContext (not yet saved — the caller commits it inside the workflow
/// transaction). On failure the HTTP-shaped fields describe a deterministic business error.
/// </summary>
public sealed record AdjustmentStageResult
{
    public ApprovalBatchAdjustment? Cycle { get; init; }
    public int ErrorStatus { get; init; }
    public string? ErrorTitle { get; init; }
    public string? ErrorDetail { get; init; }

    public bool Success => Cycle != null;

    public static AdjustmentStageResult Ok(ApprovalBatchAdjustment cycle) => new() { Cycle = cycle };
    public static AdjustmentStageResult Fail(int status, string title, string detail) =>
        new() { ErrorStatus = status, ErrorTitle = title, ErrorDetail = detail };
}

/// <summary>
/// Adjustment V2 — Phase 3 structured cycle lifecycle. Creates the canonical
/// <see cref="ApprovalBatchAdjustment"/> record for NEW adjustments and closes the open cycle from
/// the legacy resubmit/cancel paths (Phase 3 transitional compatibility — see the service remarks).
///
/// <para>All methods only STAGE changes on the injected DbContext; the caller owns the transaction
/// and <c>SaveChangesAsync</c> so cycle persistence is atomic with the batch transition.</para>
/// </summary>
public interface IAdjustmentCycleService
{
    /// <summary>
    /// Validates the structured request, guards the one-open-cycle rule, allocates the next
    /// CycleNumber and ADDS a new <see cref="ApprovalBatchAdjustment"/> (+ reason rows) to the
    /// context — all without saving. Phase 3 routes every new cycle to WAITING_BUYER (see remarks).
    /// The caller must handle the unique-constraint race at SaveChanges via
    /// <see cref="IsUniqueViolation"/>.
    /// </summary>
    Task<AdjustmentStageResult> StageNewCycleAsync(
        ApprovalBatch batch, string sourceStage, BatchAdjustmentRequestDto dto, Guid actorId,
        CancellationToken ct = default);

    /// <summary>
    /// Phase 3 transitional compatibility: closes the batch's open V2 cycle (if any) into a terminal
    /// state (RESUBMITTED from the legacy resubmit path, CANCELLED from batch cancellation) so the
    /// one-open-cycle guard never permanently blocks a second adjustment before Phase 4 owns the
    /// structured Buyer resolution/resubmit flow. Stages the change only; caller saves. Returns the
    /// closed cycle or null when there was no open cycle.
    /// </summary>
    Task<ApprovalBatchAdjustment?> CloseOpenCycleAsync(
        Guid batchId, string terminalStatus, Guid actorId, string? cancelReason,
        CancellationToken ct = default);

    /// <summary>True when the exception is a SQL unique-constraint violation — i.e. a concurrent
    /// request already created the open cycle / claimed the CycleNumber. Maps to a 409. Accepts the
    /// base <see cref="Exception"/> so the Application layer stays free of an EF Core reference; the
    /// caller passes the caught <c>DbUpdateException</c>.</summary>
    bool IsUniqueViolation(Exception ex);
}
