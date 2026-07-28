using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Projections;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Unit tests for <see cref="FinalApprovalLotViewBuilder"/> — the normalized, lot-aware view model
/// behind the Final Approval screen. Modeled on REQ-15/07/2026-075: one requested item, one included
/// EXTRA_ITEM, one IGNORED line, single supplier.
/// </summary>
public class FinalApprovalLotViewBuilderTests
{
    private const string Supplier = "Hodie Cras (SU), Lda";

    private static readonly Guid Qi450 = Guid.NewGuid();   // requested item winner (450GR)
    private static readonly Guid Qi2Kg = Guid.NewGuid();   // included EXTRA_ITEM (2KG)
    private static readonly Guid Qi1Kg = Guid.NewGuid();   // IGNORED (1KG)
    private static readonly Guid Li450 = Guid.NewGuid();
    private static readonly Guid Li2Kg = Guid.NewGuid();

    private static SavedQuotationItemDto Item(Guid id, string desc, decimal qty, decimal lineTotal, string status) => new()
    {
        Id = id,
        Description = desc,
        Quantity = qty,
        LineTotal = lineTotal,
        UnitCode = "UN",
        ReconciliationStatus = status
    };

    private static SavedQuotationDto Quotation(string supplier, params SavedQuotationItemDto[] items) => new()
    {
        Id = Guid.NewGuid(),
        SupplierNameSnapshot = supplier,
        Items = items.ToList()
    };

    private static RequestApprovalBatchDto Batch(decimal? approvedTotal, List<RequestApprovalBatchItemDto> items, List<BatchInformationalItemDto>? ignored = null) => new()
    {
        Id = Guid.NewGuid(),
        BatchNumber = 1,
        Status = "WAITING_FINAL_APPROVAL",
        ApprovedTotalAmount = approvedTotal,
        Items = items,
        IgnoredLines = ignored ?? new List<BatchInformationalItemDto>()
    };

    private static (RequestApprovalBatchDto batch, List<SavedQuotationDto> quotations) Req075Fixture(decimal? approvedTotal = 4_379_082m)
    {
        var quotation = Quotation(Supplier,
            Item(Qi450, "Martelo de Bronze/Latão 500g", 1, 186_846m, "MAPPED"),
            Item(Qi2Kg, "[Item Adicional] MARRETA OCTOGONAL BRONZE 2KG", 6, 4_192_236m, "EXTRA_ITEM"),
            Item(Qi1Kg, "MARRETA OCTOGONAL BRONZE 1KG", 3, 2_310_552m, "IGNORED"));

        var ignored = new List<BatchInformationalItemDto>
        {
            new() { QuotationItemId = Qi1Kg, Description = "MARRETA OCTOGONAL BRONZE 1KG", LineTotal = 2_310_552m,
                    SupplierName = Supplier, ReconciliationJustification = "Item não relacionado ao pedido" }
        };

        var batch = Batch(approvedTotal, new List<RequestApprovalBatchItemDto>
        {
            new() { Id = Guid.NewGuid(), RequestLineItemId = Li450, SelectedQuotationItemId = Qi450 },
            new() { Id = Guid.NewGuid(), RequestLineItemId = Li2Kg, SelectedQuotationItemId = Qi2Kg }
        }, ignored);

        return (batch, new List<SavedQuotationDto> { quotation });
    }

    [Fact] // (1) requested batch item uses SelectedQuotationItem.LineTotal, never the RequestLineItem estimate
    public void RequestedItem_UsesSelectedQuotationLineTotal()
    {
        var (batch, quotations) = Req075Fixture();
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        var requested = view.IncludedItems.Single(i => i.RequestLineItemId == Li450);
        Assert.Equal(186_846m, requested.LineTotal);
        Assert.False(requested.IsExtraItem);
    }

    [Fact] // (2) EXTRA_ITEM stays included with its own line total and is flagged
    public void ExtraItem_IsIncludedWithLineTotalAndFlagged()
    {
        var (batch, quotations) = Req075Fixture();
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        var extra = view.IncludedItems.Single(i => i.RequestLineItemId == Li2Kg);
        Assert.Equal(4_192_236m, extra.LineTotal);
        Assert.True(extra.IsExtraItem);
    }

    [Fact] // (3) IGNORED line is excluded from the lot item count and total
    public void IgnoredLine_ExcludedFromCountAndTotal()
    {
        var (batch, quotations) = Req075Fixture();
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(2, view.IncludedItemCount);
        Assert.DoesNotContain(view.IncludedItems, i => i.SelectedQuotationItemId == Qi1Kg);
        Assert.Equal(4_379_082m, view.LotTotal);
    }

    [Fact] // (4) IGNORED line remains available in audit data
    public void IgnoredLine_RemainsInAuditData()
    {
        var (batch, quotations) = Req075Fixture();
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(1, view.IgnoredItemCount);
        var ignored = Assert.Single(view.IgnoredLines);
        Assert.Equal(Qi1Kg, ignored.QuotationItemId);
        Assert.Equal(2_310_552m, ignored.LineTotal);
    }

    [Fact] // (5) single-supplier lot resolves the supplier header
    public void SingleSupplier_ResolvesHeader()
    {
        var (batch, quotations) = Req075Fixture();
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(Supplier, view.SupplierLabel);
        Assert.Equal("Fornecedor do lote", view.SupplierHeading);
        Assert.Single(view.SupplierNames);
    }

    [Fact] // (6) multi-supplier lot produces a count label
    public void MultiSupplier_ProducesCountLabel()
    {
        var qiA = Guid.NewGuid();
        var qiB = Guid.NewGuid();
        var liA = Guid.NewGuid();
        var liB = Guid.NewGuid();

        var quotations = new List<SavedQuotationDto>
        {
            Quotation("Supplier A", Item(qiA, "Item A", 1, 100m, "MAPPED")),
            Quotation("Supplier B", Item(qiB, "Item B", 1, 200m, "MAPPED"))
        };
        var batch = Batch(null, new List<RequestApprovalBatchItemDto>
        {
            new() { Id = Guid.NewGuid(), RequestLineItemId = liA, SelectedQuotationItemId = qiA },
            new() { Id = Guid.NewGuid(), RequestLineItemId = liB, SelectedQuotationItemId = qiB }
        });

        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal("2 fornecedores", view.SupplierLabel);
        Assert.Equal("Fornecedores do lote", view.SupplierHeading);
    }

    [Fact] // (9a) with no snapshot, lot total equals the sum of included line totals
    public void LotTotal_FallsBackToSumOfIncluded_WhenNoSnapshot()
    {
        var (batch, quotations) = Req075Fixture(approvedTotal: null);
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(186_846m + 4_192_236m, view.LotTotal);
        Assert.False(view.HasMonetaryInconsistency);
    }

    [Fact] // (9b) a snapshot that disagrees with the item sum surfaces an inconsistency, not a false total
    public void LotTotal_FlagsInconsistency_WhenSnapshotDisagrees()
    {
        var (batch, quotations) = Req075Fixture(approvedTotal: 9_999_999m);
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(9_999_999m, view.LotTotal);
        Assert.True(view.HasMonetaryInconsistency);
    }

    [Fact] // (10) partial lot never pulls in items outside the batch
    public void PartialLot_ExcludesItemsNotInBatch()
    {
        var (batch, quotations) = Req075Fixture();
        // The quotation carries the IGNORED 1KG line and could carry others; only the two batch
        // items must appear.
        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.Equal(2, view.IncludedItems.Count);
        Assert.All(view.IncludedItems, i =>
            Assert.Contains(batch.Items, bi => bi.SelectedQuotationItemId == i.SelectedQuotationItemId));
    }

    [Fact] // defensive: an unresolvable winner never silently becomes 0
    public void UnresolvableWinner_YieldsNullTotalAndFlag()
    {
        var quotations = new List<SavedQuotationDto>
        {
            Quotation(Supplier, Item(Qi450, "Item", 1, 186_846m, "MAPPED"))
        };
        var batch = Batch(null, new List<RequestApprovalBatchItemDto>
        {
            new() { Id = Guid.NewGuid(), RequestLineItemId = Li450, SelectedQuotationItemId = Qi450 },
            new() { Id = Guid.NewGuid(), RequestLineItemId = Li2Kg, SelectedQuotationItemId = Guid.NewGuid() } // missing
        });

        var view = FinalApprovalLotViewBuilder.Build(batch, quotations);

        Assert.True(view.HasUnresolvedItemValue);
        Assert.Contains(view.IncludedItems, i => i.LineTotal == null);
    }
}
