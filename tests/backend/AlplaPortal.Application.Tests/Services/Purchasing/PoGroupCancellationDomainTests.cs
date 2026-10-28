using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;
using E = AlplaPortal.Domain.Services.PoGroupCancellationEvaluator;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>Pure rules of phase 1: the cancellation evaluator (approved matrix §K.1 + D11), the ECF-requirement rule
/// (§D.1), the D5 active totals (never summed across currencies), the departmental resolver's ActiveApproved source and
/// the Buyer-queue projection with released / cancelled-after-approval items.</summary>
public class PoGroupCancellationDomainTests
{
    private static E.Input Input(string groupStatus = "PO_ISSUED", string type = "QUOTATION", string requestStatus = "PO_ISSUED", bool cancelled = false,
        bool alreadyCancelled = false, IEnumerable<E.LineFacts>? lines = null, IEnumerable<E.PaymentFacts>? payments = null,
        bool invoice = false, bool fiscal = false, bool receiptStamp = false, bool linkage = false)
        => new(type, requestStatus, cancelled, groupStatus, alreadyCancelled,
            (lines ?? new[] { new E.LineFacts(Guid.NewGuid(), 1, "WAITING_QUOTATION", 0m, false) }).ToList(),
            (payments ?? Array.Empty<E.PaymentFacts>()).ToList(), invoice, fiscal, receiptStamp, linkage);

    [Theory]
    [InlineData("WAITING_PO")]
    [InlineData("WAITING_PO_CORRECTION")]
    [InlineData("PO_ISSUED")]
    [InlineData("ADVANCE_PAYMENT_REQUIRED")]
    public void Cancellable_statuses(string status) => Assert.True(E.Evaluate(Input(status)).CanCancel);

    [Theory]
    [InlineData("PAYMENT_SCHEDULED", C.ErrorCodes.PaymentScheduledActive)]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED", C.ErrorCodes.PaymentScheduledActive)]
    [InlineData("PENDING", C.ErrorCodes.GroupStatusNotCancellable)]
    [InlineData("ADVANCE_PAYMENT_COMPLETED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("WAITING_SUPPLIER_DELIVERY", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("PAYMENT_COMPLETED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("WAITING_RECEIPT", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("COMPLETED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("CANCELLED", C.ErrorCodes.GroupAlreadyCancelled)]
    public void Refused_statuses(string status, string code)
    {
        var r = E.Evaluate(Input(status));
        Assert.False(r.CanCancel); Assert.Equal(code, r.Code);
    }

    [Fact]
    public void Payment_type_request_is_out_of_phase_1() => Assert.Equal(C.ErrorCodes.RequestTypeNotSupported, E.Evaluate(Input(type: "PAYMENT")).Code);

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    [InlineData("COMPLETED")]
    public void Terminal_request_refused(string status) => Assert.Equal(C.ErrorCodes.RequestTerminal, E.Evaluate(Input(requestStatus: status)).Code);

    [Fact]
    public void Completed_or_scheduled_payment_rows_refuse_even_on_a_cancellable_status()
    {
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, E.Evaluate(Input(payments: new[] { new E.PaymentFacts("ADVANCE", "COMPLETED") })).Code);
        Assert.Equal(C.ErrorCodes.PaymentScheduledActive, E.Evaluate(Input(payments: new[] { new E.PaymentFacts("ADVANCE", "SCHEDULED") })).Code);
        Assert.True(E.Evaluate(Input(payments: new[] { new E.PaymentFacts("ADVANCE", "PLANNED"), new E.PaymentFacts("ADVANCE", "CANCELLED") })).CanCancel);
    }

    [Fact]
    public void Invoice_fiscal_receipt_or_receipt_stamp_refuse()
    {
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, E.Evaluate(Input(invoice: true)).Code);
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, E.Evaluate(Input(fiscal: true)).Code);
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, E.Evaluate(Input(receiptStamp: true)).Code);
    }

    [Fact]
    public void Received_lines_refuse_and_ORDERED_requires_investigation_without_touching_lines()
    {
        var received = E.Evaluate(Input(lines: new[] { new E.LineFacts(Guid.NewGuid(), 3, "RECEIVED", 1m, false) }));
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, received.Code); Assert.Contains("#3", received.Message);
        var qty = E.Evaluate(Input(lines: new[] { new E.LineFacts(Guid.NewGuid(), 4, "WAITING_ORDER", 0.5m, false) }));
        Assert.Equal(C.ErrorCodes.PaymentOrReceiptEvidence, qty.Code);
        var ordered = E.Evaluate(Input(lines: new[] { new E.LineFacts(Guid.NewGuid(), 7, "ORDERED", 0m, false) }));
        Assert.Equal(C.ErrorCodes.OrderedLineRequiresInvestigation, ordered.Code);
        Assert.Contains("#7", ordered.Message); Assert.Contains("investigação", ordered.Message); Assert.Contains("não é alterado automaticamente", ordered.Message);
        // a deleted ORDERED line is ignored
        Assert.True(E.Evaluate(Input(lines: new[] { new E.LineFacts(Guid.NewGuid(), 7, "ORDERED", 0m, true), new E.LineFacts(Guid.NewGuid(), 8, "PENDING", 0m, false) })).CanCancel);
    }

    [Fact]
    public void Broken_linkage_refuses() => Assert.Equal(C.ErrorCodes.GroupLinkageInconsistent, E.Evaluate(Input(linkage: true)).Code);

    [Fact]
    public void Restorable_line_statuses()
    {
        Assert.True(E.IsRestorableLineStatus(null)); Assert.True(E.IsRestorableLineStatus("WAITING_ORDER")); Assert.True(E.IsRestorableLineStatus("PENDING"));
        Assert.False(E.IsRestorableLineStatus("ORDERED")); Assert.False(E.IsRestorableLineStatus("RECEIVED")); Assert.False(E.IsRestorableLineStatus("CANCELLED"));
    }

    [Fact]
    public void Finance_confirmation_required_only_when_a_PO_ever_existed()
    {
        Assert.False(E.RequiresFinanceConfirmation(null, false, false));
        Assert.False(E.RequiresFinanceConfirmation("  ", false, false));
        Assert.True(E.RequiresFinanceConfirmation("ECF10 2026/364", false, false));
        Assert.True(E.RequiresFinanceConfirmation(null, true, false));   // voided P.O. file still proves a P.O. existed
        Assert.True(E.RequiresFinanceConfirmation(null, false, true));   // REGISTER_PO history alone
    }

    // ── D5 active totals ──

    [Fact]
    public void Active_totals_exclude_cancelled_groups_and_never_sum_currencies()
    {
        var approved = new ApprovalBatch { Id = Guid.NewGuid(), Status = "APPROVED" };
        var inFlight = new ApprovalBatch { Id = Guid.NewGuid(), Status = "WAITING_FINAL_APPROVAL" };
        var groups = new[]
        {
            new RequestPoGroup { ApprovalBatchId = approved.Id, Status = "CANCELLED", CurrencyCode = "RND", TotalAmount = 24133.06m },
            new RequestPoGroup { ApprovalBatchId = approved.Id, Status = "WAITING_PO", CurrencyCode = "AOA", TotalAmount = 500m },
            new RequestPoGroup { ApprovalBatchId = approved.Id, Status = "PO_ISSUED", CurrencyCode = "aoa", TotalAmount = 250.004m },
            new RequestPoGroup { ApprovalBatchId = inFlight.Id, Status = "PENDING", CurrencyCode = "USD", TotalAmount = 99m },   // not approved → not active
            new RequestPoGroup { ApprovalBatchId = null, Status = "WAITING_PO", CurrencyCode = "EUR", TotalAmount = 10m },      // batchless (PAYMENT-style) counts
        };
        var totals = ActiveApprovedTotals.ForRequest(groups, new[] { approved, inFlight });
        Assert.Equal(new[] { ("AOA", 750.00m), ("EUR", 10m) }, totals.Select(t => (t.Currency, t.Amount)).ToArray());
        var (amount, currency) = ActiveApprovedTotals.Scalar(totals);
        Assert.Null(amount); Assert.Equal("MULTIPLE", currency);
        var single = ActiveApprovedTotals.ForRequest(groups.Take(3), new[] { approved });
        Assert.Equal((750.00m, "AOA"), ActiveApprovedTotals.Scalar(single));
        Assert.Equal((null, null), ActiveApprovedTotals.Scalar(Array.Empty<ActiveApprovedTotals.CurrencyTotal>()));
    }

    [Fact]
    public void Batch_split_active_vs_released_from_frozen_candidates()
    {
        var b = new ApprovalBatch { ApprovedTotalAmount = 24633.06m };
        b.Items.Add(new ApprovalBatchItem { SelectedCandidate = new ApprovalBatchItemCandidate { LineTotal = 24133.06m, Currency = "RND" }, ReleasedAtUtc = DateTime.UtcNow });
        b.Items.Add(new ApprovalBatchItem { SelectedCandidate = new ApprovalBatchItemCandidate { LineTotal = 500m, Currency = "AOA" } });
        b.Items.Add(new ApprovalBatchItem { SelectedQuotationItem = new QuotationItem { LineTotal = 1m, Quotation = new Quotation { Currency = "AOA" } } }); // legacy item
        b.Items.Add(new ApprovalBatchItem()); // undecided → ignored
        var t = ActiveApprovedTotals.ForBatch(b);
        Assert.Equal(24633.06m, t.HistoricalApprovedTotal);
        Assert.Equal(1, t.ReleasedItemCount);
        Assert.Equal(new[] { ("AOA", 501m) }, t.Active.Select(x => (x.Currency, x.Amount)).ToArray());
        Assert.Equal(new[] { ("RND", 24133.06m) }, t.Released.Select(x => (x.Currency, x.Amount)).ToArray());
    }

    [Fact]
    public void Departmental_resolver_reports_live_groups_when_a_group_was_cancelled_or_items_released()
    {
        var facts = new RequestAmountFacts(Guid.NewGuid(), 0m, "RND", 24633.06m, "RND", new[] { "RND" }, 500m, 0, CancelledGroupCount: 1, 1, 1, 24633.06m, 1);
        var r = DepartmentalAmountResolver.Resolve(facts);
        Assert.Equal(RequestAmountSource.ActiveApproved, r.Source); Assert.Equal(500m, r.Amount); Assert.Equal("RND", r.Currency);
        var released = facts with { CancelledGroupCount = 0, ReleasedBatchItemCount = 1 };
        Assert.Equal(RequestAmountSource.ActiveApproved, DepartmentalAmountResolver.Resolve(released).Source);
        var untouched = facts with { CancelledGroupCount = 0 };
        Assert.Equal(RequestAmountSource.RecordedApproved, DepartmentalAmountResolver.Resolve(untouched).Source);
        Assert.Equal(24633.06m, DepartmentalAmountResolver.Resolve(untouched).Amount);
        var nothingLive = facts with { LiveGroupCurrencies = Array.Empty<string?>(), LiveGroupSum = 0m };
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(nothingLive).Source);
    }

    // ── Buyer queue projection ──

    [Fact]
    public void Released_batch_items_no_longer_hold_their_candidates_and_closed_lines_bucket_as_cancelled()
    {
        var line = Guid.NewGuid(); var closed = Guid.NewGuid(); var qi = Guid.NewGuid(); var qiAlt = Guid.NewGuid();
        var input = new BuyerQueueProjectionBuilder.RequestInput(Guid.NewGuid(), "REQ", "t", "QUOTATION", "WAITING_QUOTATION", false, Guid.NewGuid(), null, null, DateTime.UtcNow, false, false,
            new[]
            {
                new BuyerQueueProjectionBuilder.ItemInput(line, false, RequestConstants.QuotationLifecycleStatuses.QuotationPending, "WAITING_QUOTATION", false),
                new BuyerQueueProjectionBuilder.ItemInput(closed, false, RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval, "CANCELLED", false),
            },
            new[] { new BuyerQueueProjectionBuilder.BatchInput(Guid.NewGuid(), 1, "APPROVED", new[] { new BuyerQueueProjectionBuilder.BatchItemInput(line, qi, new[] { qi, qiAlt }, IsReleased: true) }) },
            new[] { new BuyerQueueProjectionBuilder.QuotationItemInput(qi, line, "MAPPED"), new BuyerQueueProjectionBuilder.QuotationItemInput(qiAlt, line, "MAPPED") },
            new HashSet<Guid>());
        var buckets = BuyerQueueProjectionBuilder.ClassifyItemCoverage(input);
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch, buckets[line]);   // candidates selectable again
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.CancelledDeleted, buckets[closed]);
        var held = input with { Batches = new[] { new BuyerQueueProjectionBuilder.BatchInput(Guid.NewGuid(), 1, "APPROVED", new[] { new BuyerQueueProjectionBuilder.BatchItemInput(line, qi, new[] { qi, qiAlt }) }) } };
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.PendingQuotation, BuyerQueueProjectionBuilder.ClassifyItemCoverage(held)[line]);   // not released → still held
    }

    [Fact]
    public void Po_correction_state_offers_correct_and_cancel()
    {
        var line = Guid.NewGuid();
        var input = new BuyerQueueProjectionBuilder.RequestInput(Guid.NewGuid(), "REQ", "t", "QUOTATION", "WAITING_PO_CORRECTION", false, Guid.NewGuid(), null, null, DateTime.UtcNow, false, false,
            new[] { new BuyerQueueProjectionBuilder.ItemInput(line, false, RequestConstants.QuotationLifecycleStatuses.QuotationApproved, "WAITING_ORDER", false) },
            Array.Empty<BuyerQueueProjectionBuilder.BatchInput>(), Array.Empty<BuyerQueueProjectionBuilder.QuotationItemInput>(), new HashSet<Guid>(), HasPoCorrection: true);
        var p = BuyerQueueProjectionBuilder.Build(input, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(new[] { "CORRECT_PO", "CANCEL_PO_GROUP" }, p.NextBuyerActions.Select(a => a.Code).ToArray());
        Assert.All(p.NextBuyerActions, a => Assert.True(a.Actionable));
    }

    [Fact]
    public void Reason_and_kind_catalogues()
    {
        Assert.True(C.ReasonCodes.IsValid("SUPPLIER_CANCELLED")); Assert.False(C.ReasonCodes.IsValid("NOPE")); Assert.False(C.ReasonCodes.IsValid(null));
        Assert.Equal("Outro (explicar)", C.ReasonCodes.Label("OTHER"));
        Assert.Equal("Tipo não classificado (devolução anterior à versão)", C.FinanceReturnKinds.Label(null));
        Assert.Equal("Cancelamento solicitado — não corrigir", C.FinanceReturnKinds.Label("ORDER_CANCELLATION_REQUESTED"));
        Assert.Equal(20, C.MinJustificationLength);
    }
}
