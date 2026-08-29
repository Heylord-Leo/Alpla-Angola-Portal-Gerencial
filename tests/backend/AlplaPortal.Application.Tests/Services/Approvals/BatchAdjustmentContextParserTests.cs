using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;
using Entry = AlplaPortal.Domain.Services.BatchAdjustmentContextParser.HistoryEntry;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Adjustment V2 Phase 1 (QF1) — the parser that derives the Approver's ACTUAL adjustment context
/// from status history. Regression anchor: the rework UI previously displayed batch.Comment (the
/// Buyer's own text) as "Motivo do reajuste"; these tests lock the real source and its fail-safe
/// behavior (unparseable legacy history ⇒ null fields, never invented data).
/// </summary>
public class BatchAdjustmentContextParserTests
{
    private static readonly DateTime T0 = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Entry Area(int batchNumber, string reason, DateTime at, string actor = "Maria S.") =>
        new(BatchAdjustmentContextParser.AreaAdjustmentAction,
            $"Solicitado reajuste no Lote #{batchNumber} na Aprovação da Área. Motivo: {reason}", at, actor);

    private static Entry Final(int batchNumber, string reason, DateTime at, string actor = "Carlos F.") =>
        new(BatchAdjustmentContextParser.FinalAdjustmentAction,
            $"Solicitado reajuste final no Lote #{batchNumber}. Motivo: {reason}", at, actor);

    [Fact]
    public void AreaAdjustment_ParsesReasonActorTimestampAndStage()
    {
        var ctx = BatchAdjustmentContextParser.Resolve(new[] { Area(1, "Valor acima do orçamento.", T0) }, batchNumber: 1);

        Assert.NotNull(ctx);
        Assert.Equal("Valor acima do orçamento.", ctx!.Reason);
        Assert.Equal("Maria S.", ctx.RequestedByName);
        Assert.Equal(T0, ctx.RequestedAtUtc);
        Assert.Equal(BatchAdjustmentContextParser.SourceStageArea, ctx.SourceStage);
    }

    [Fact]
    public void FinalAdjustment_ReportsFinalStage()
    {
        var ctx = BatchAdjustmentContextParser.Resolve(new[] { Final(2, "Rever condição de pagamento.", T0) }, batchNumber: 2);

        Assert.NotNull(ctx);
        Assert.Equal("Rever condição de pagamento.", ctx!.Reason);
        Assert.Equal(BatchAdjustmentContextParser.SourceStageFinal, ctx.SourceStage);
    }

    [Fact]
    public void MultipleCycles_LatestEntryWins_RegardlessOfInputOrder()
    {
        // GetRequest materializes history DESC, but the parser must not depend on any order.
        var history = new[]
        {
            Area(1, "Motivo mais recente.", T0.AddDays(2), actor: "Maria S."),
            Area(1, "Motivo antigo.", T0),
            Final(1, "Motivo intermédio.", T0.AddDays(1))
        };

        var ctx = BatchAdjustmentContextParser.Resolve(history, batchNumber: 1);

        Assert.NotNull(ctx);
        Assert.Equal("Motivo mais recente.", ctx!.Reason);
        Assert.Equal(BatchAdjustmentContextParser.SourceStageArea, ctx.SourceStage);
        Assert.Equal(T0.AddDays(2), ctx.RequestedAtUtc);
    }

    [Fact]
    public void OtherBatchesHistory_IsNeverAttributed()
    {
        // Batch #1 must not read batch #2's entry — and "Lote #12" must not satisfy batch 1.
        var history = new[] { Area(2, "Motivo do lote dois.", T0), Area(12, "Motivo do lote doze.", T0.AddHours(1)) };

        Assert.Null(BatchAdjustmentContextParser.Resolve(history, batchNumber: 1));

        var ctx2 = BatchAdjustmentContextParser.Resolve(history, batchNumber: 2);
        Assert.Equal("Motivo do lote dois.", ctx2!.Reason);
    }

    [Fact]
    public void MalformedOrMissingLegacyHistory_ReturnsNullFieldsSafely()
    {
        // No history at all.
        Assert.Null(BatchAdjustmentContextParser.Resolve(null, 1));
        Assert.Null(BatchAdjustmentContextParser.Resolve(Array.Empty<Entry>(), 1));

        // Unrelated actions are ignored.
        Assert.Null(BatchAdjustmentContextParser.Resolve(
            new[] { new Entry("BATCH_RESUBMITTED", "Lote #1 reenviado para aprovação da área.", T0, "João M.") }, 1));

        // Adjustment entry without the "Motivo: " marker: entry still attributes (stage/actor/date)
        // but the reason is null — never invented.
        var noMarker = new Entry(BatchAdjustmentContextParser.AreaAdjustmentAction, "Solicitado reajuste no Lote #1.", T0, "Maria S.");
        var ctx = BatchAdjustmentContextParser.Resolve(new[] { noMarker }, 1);
        Assert.NotNull(ctx);
        Assert.Null(ctx!.Reason);
        Assert.Equal("Maria S.", ctx.RequestedByName);

        // Adjustment entry without any "Lote #N" reference cannot be attributed to a batch.
        var noBatchRef = new Entry(BatchAdjustmentContextParser.AreaAdjustmentAction, "Solicitado reajuste. Motivo: X", T0, "Maria S.");
        Assert.Null(BatchAdjustmentContextParser.Resolve(new[] { noBatchRef }, 1));

        // Empty reason after the marker and blank actor collapse to null.
        var blank = new Entry(BatchAdjustmentContextParser.AreaAdjustmentAction, "Solicitado reajuste no Lote #1 na Aprovação da Área. Motivo: ", T0, "  ");
        var blankCtx = BatchAdjustmentContextParser.Resolve(new[] { blank }, 1);
        Assert.NotNull(blankCtx);
        Assert.Null(blankCtx!.Reason);
        Assert.Null(blankCtx.RequestedByName);
    }
}
