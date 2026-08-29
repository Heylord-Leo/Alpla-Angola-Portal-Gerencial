using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Adjustment V2 Phase 1 (QF1): derives the Approver's actual adjustment context for one batch
/// from the request's status history. The reason a batch entered AREA_ADJUSTMENT/FINAL_ADJUSTMENT
/// is recorded ONLY as a history entry (batch.Comment is the Buyer's own text and must never be
/// presented as the adjustment motive), so until the V2 adjustment entity exists this parser is
/// the single authoritative reader of that history. Read-only: never mutates or backfills rows.
/// Interim by design — superseded by ApprovalBatchAdjustment in V2 Phase 2+.
/// </summary>
public static class BatchAdjustmentContextParser
{
    // Same literals ApprovalBatchController writes. The "Motivo: " suffix and "Lote #N" reference
    // are owned by this codebase (BatchAreaRequestAdjustment / BatchFinalRequestAdjustment).
    public const string AreaAdjustmentAction = "BATCH_AREA_ADJUSTMENT";
    public const string FinalAdjustmentAction = "BATCH_FINAL_ADJUSTMENT";

    public const string SourceStageArea = "AREA";
    public const string SourceStageFinal = "FINAL";

    private const string ReasonMarker = "Motivo: ";

    // "Lote #12" must never satisfy batch 1 — the number is captured and compared exactly.
    private static readonly Regex BatchNumberPattern = new(@"Lote #(\d+)", RegexOptions.Compiled);

    /// <summary>One status-history entry, reduced to the fields this parser needs.</summary>
    public sealed record HistoryEntry(string? ActionTaken, string? Comment, DateTime CreatedAtUtc, string? ActorName);

    /// <summary>The Approver's adjustment context for one batch. Fields are null when the
    /// underlying legacy text cannot be parsed safely — never invented.</summary>
    public sealed record AdjustmentContext(string? Reason, string SourceStage, string? RequestedByName, DateTime RequestedAtUtc);

    /// <summary>
    /// Returns the context of the MOST RECENT adjustment request recorded for
    /// <paramref name="batchNumber"/>, or null when the history holds none (batch was never
    /// adjusted, or its entries cannot be attributed to this batch safely).
    /// </summary>
    public static AdjustmentContext? Resolve(IEnumerable<HistoryEntry>? history, int batchNumber)
    {
        if (history == null) return null;

        HistoryEntry? latest = null;
        string? latestStage = null;

        foreach (var entry in history)
        {
            var stage = entry.ActionTaken switch
            {
                AreaAdjustmentAction => SourceStageArea,
                FinalAdjustmentAction => SourceStageFinal,
                _ => null
            };
            if (stage == null) continue;
            if (!MentionsBatch(entry.Comment, batchNumber)) continue;

            if (latest == null || entry.CreatedAtUtc > latest.CreatedAtUtc)
            {
                latest = entry;
                latestStage = stage;
            }
        }

        if (latest == null || latestStage == null) return null;

        var actorName = string.IsNullOrWhiteSpace(latest.ActorName) ? null : latest.ActorName.Trim();
        return new AdjustmentContext(ExtractReason(latest.Comment), latestStage, actorName, latest.CreatedAtUtc);
    }

    private static bool MentionsBatch(string? comment, int batchNumber)
    {
        if (string.IsNullOrEmpty(comment)) return false;
        var match = BatchNumberPattern.Match(comment);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var mentioned)
            && mentioned == batchNumber;
    }

    private static string? ExtractReason(string? comment)
    {
        if (string.IsNullOrEmpty(comment)) return null;
        var idx = comment.IndexOf(ReasonMarker, StringComparison.Ordinal);
        if (idx < 0) return null; // legacy/unexpected format — null, never invented
        var reason = comment[(idx + ReasonMarker.Length)..].Trim();
        return reason.Length == 0 ? null : reason;
    }
}
