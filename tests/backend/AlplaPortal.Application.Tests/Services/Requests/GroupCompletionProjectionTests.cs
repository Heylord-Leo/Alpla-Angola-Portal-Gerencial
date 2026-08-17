using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 4 Phase 4A: the approved group completion predicate, pinned case by case (matrix
/// A–S of the Phase 4A instruction). The projector is the SINGLE rulebook shared by the Phase 1
/// evaluation, the future readiness UI and the future parent completion — these tests are the
/// contract all three read.
/// </summary>
public class GroupCompletionProjectionTests
{
    private static readonly LineItemStatus Received = new() { Id = 91, Code = "RECEIVED", Name = "Recebido" };
    private static readonly LineItemStatus Partially = new() { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" };
    private static readonly LineItemStatus Pending = new() { Id = 93, Code = "PENDING", Name = "Pendente" };

    /// <summary>
    /// A classified, paid, fully received group whose invoice obligation is satisfied and whose
    /// separate fiscal receipt is required and uploaded — every boolean true. Each test breaks
    /// exactly one dimension.
    /// </summary>
    private static RequestPoGroup SatisfiedGroup(
        string status = RequestConstants.PoGroupStatuses.WaitingReceipt,
        string operationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
        string? sourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
        bool separateFiscalReceipt = true,
        bool fiscalReceiptUploaded = true,
        bool receiptStamped = true,
        bool requiresAdvanceRegularization = false,
        params LineItemStatus?[] itemStatuses)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            SupplierNameSnapshot = "ZZTEST Projection Supplier",
            Status = status,
            SourceDocumentType = sourceDocumentType,
            OperationInvoiceStatus = operationInvoiceStatus,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = separateFiscalReceipt,
            RequiresAdvanceRegularization = requiresAdvanceRegularization,
            OperationalReceiptCompletedAtUtc = receiptStamped ? DateTime.UtcNow.AddDays(-1) : null,
            FiscalReceiptAttachmentId = separateFiscalReceipt && fiscalReceiptUploaded ? Guid.NewGuid() : null,
            FiscalReceiptUploadedAtUtc = separateFiscalReceipt && fiscalReceiptUploaded ? DateTime.UtcNow : null
        };

        var line = 1;
        foreach (var itemStatus in itemStatuses)
        {
            group.LineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = group.RequestId,
                RequestPoGroupId = group.Id,
                LineNumber = line++,
                Description = "ZZTEST item",
                Quantity = 1,
                LineItemStatusId = itemStatus?.Id,
                LineItemStatus = itemStatus
            });
        }

        return group;
    }

    private static RequestPayment Payment(
        Guid? groupId, string type, string paymentStatus) => new()
        {
            RequestPoGroupId = groupId,
            PaymentType = type,
            PaymentStatus = paymentStatus,
            PlannedAmount = 100m,
            CurrencyCode = "AOA"
        };

    private static RequestReconciliation Reconciliation(Guid? groupId, string status) => new()
    {
        RequestPoGroupId = groupId,
        ReconciliationStatus = status
    };

    private static GroupCompletionProjection Project(
        RequestPoGroup group,
        IEnumerable<RequestPayment>? payments = null,
        IEnumerable<RequestReconciliation>? reconciliations = null,
        bool hasApprovedShortClose = false) =>
        GroupCompletionProjector.Project(
            group,
            payments ?? Array.Empty<RequestPayment>(),
            reconciliations ?? Array.Empty<RequestReconciliation>(),
            hasApprovedShortClose);

    // ── A: unclassified fails closed ──

    [Fact]
    public void A_unclassified_group_is_blocked_and_never_complete()
    {
        var group = SatisfiedGroup(
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Unclassified,
            sourceDocumentType: null);

        var p = Project(group);

        Assert.False(p.Classified);
        Assert.False(p.Complete);
        Assert.False(p.ReadyForFiscalReceipt);
        Assert.Equal(GroupCompletionBlockingReasons.ClassificationPending, p.BlockingReasons.First());
    }

    [Fact]
    public void A2_classified_status_with_null_source_document_still_fails_closed()
    {
        // Both facts are required: a stamped aggregate status with no recorded identity is
        // drift, and drift must block rather than complete.
        var group = SatisfiedGroup(sourceDocumentType: null);

        var p = Project(group);

        Assert.False(p.Classified);
        Assert.Contains(GroupCompletionBlockingReasons.ClassificationPending, p.BlockingReasons);
    }

    // ── B/C: the P.O. family ──

    [Fact]
    public void B_group_still_waiting_po_is_not_po_satisfied()
    {
        var group = SatisfiedGroup(status: RequestConstants.PoGroupStatuses.WaitingPo);

        var p = Project(group);

        Assert.False(p.PoSatisfied);
        Assert.False(p.Complete);
        Assert.Contains(GroupCompletionBlockingReasons.PoMissing, p.BlockingReasons);
    }

    [Fact]
    public void C_po_correction_is_an_independent_hard_blocker()
    {
        var group = SatisfiedGroup(status: RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var p = Project(group);

        // The P.O. itself was registered (the group left WAITING_PO)…
        Assert.True(p.PoSatisfied);
        // …but the correction parks everything.
        Assert.False(p.NoBlockingCorrection);
        Assert.False(p.Complete);
        Assert.Contains(GroupCompletionBlockingReasons.PoCorrectionPending, p.BlockingReasons);
    }

    // ── D/E/F: payment rows — SCHEDULED is never paid ──

    [Fact]
    public void D_request_level_planned_final_balance_blocks_every_group()
    {
        // A reconciliation-born FINAL_BALANCE carries no group id. Attributing it to nobody
        // would silently complete a request that still owes money — so it blocks all groups.
        var group = SatisfiedGroup();
        var payments = new[] { Payment(null, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Planned) };

        var p = Project(group, payments);

        Assert.False(p.PaymentSatisfied);
        Assert.False(p.Complete);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending, p.BlockingReasons);
    }

    [Fact]
    public void E_scheduled_group_payment_is_not_paid()
    {
        var group = SatisfiedGroup();
        var payments = new[] { Payment(group.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Scheduled) };

        var p = Project(group, payments);

        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending, p.BlockingReasons);
    }

    [Fact]
    public void F_completed_payment_rows_do_not_block()
    {
        var group = SatisfiedGroup();
        var payments = new[]
        {
            Payment(group.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed),
            Payment(group.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed),
            // Cancelled owed rows and refunds are not outstanding obligations either.
            Payment(group.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Cancelled),
            Payment(group.Id, RequestPayment.PaymentTypes.Refund, RequestPayment.PaymentStatuses.Planned)
        };

        var p = Project(group, payments);

        Assert.True(p.PaymentSatisfied);
        Assert.True(p.Complete);
    }

    [Fact]
    public void F2_group_not_at_paid_stage_is_not_payment_satisfied_even_with_no_rows()
    {
        // No payment rows at all does not mean paid: the group must have actually reached the
        // paid stage. PO_ISSUED has not.
        var group = SatisfiedGroup(status: RequestConstants.PoGroupStatuses.PoIssued);

        var p = Project(group);

        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending, p.BlockingReasons);
    }

    [Fact]
    public void F3_waiting_reconciliation_is_not_a_paid_stage()
    {
        // An advance under reconciliation may still owe a final balance.
        var group = SatisfiedGroup(status: RequestConstants.PoGroupStatuses.WaitingReconciliation);

        var p = Project(group);

        Assert.False(p.PaymentSatisfied);
    }

    // ── G/H: reconciliation ──

    [Fact]
    public void G_active_reconciliation_blocks()
    {
        var group = SatisfiedGroup();
        var reconciliations = new[]
        {
            Reconciliation(null, RequestReconciliation.ReconciliationStatuses.InProgress)
        };

        var p = Project(group, reconciliations: reconciliations);

        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.ReconciliationPending, p.BlockingReasons);
    }

    [Fact]
    public void H_required_regularization_needs_a_completed_reconciliation()
    {
        var group = SatisfiedGroup(requiresAdvanceRegularization: true);

        var blocked = Project(group);
        Assert.False(blocked.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.ReconciliationPending, blocked.BlockingReasons);

        var discharged = Project(group, reconciliations: new[]
        {
            Reconciliation(group.Id, RequestReconciliation.ReconciliationStatuses.Completed)
        });
        Assert.True(discharged.PaymentSatisfied);
        Assert.True(discharged.Complete);
    }

    // ── I/J/K: operational receipt ──

    [Fact]
    public void I_missing_receipt_blocks()
    {
        var group = SatisfiedGroup(receiptStamped: false, itemStatuses: Pending);

        var p = Project(group);

        Assert.False(p.ReceiptSatisfied);
        Assert.False(p.Complete);
        Assert.Contains(GroupCompletionBlockingReasons.ReceiptPending, p.BlockingReasons);
    }

    [Fact]
    public void J_partial_receiving_blocks()
    {
        var group = SatisfiedGroup(receiptStamped: false, itemStatuses: new[] { Received, Partially });

        var p = Project(group);

        Assert.False(p.ReceiptSatisfied);
    }

    [Fact]
    public void K_item_records_alone_prove_the_receipt_for_pre_activation_groups()
    {
        // No stamp, but every item RECEIVED: the pure projection reads the fact from the item
        // records. The stamp itself is written only by the evaluation write path, never here.
        var group = SatisfiedGroup(receiptStamped: false, itemStatuses: new[] { Received, Received });

        var p = Project(group);

        Assert.True(p.ReceiptSatisfied);
        Assert.True(p.Complete);
        Assert.Null(group.OperationalReceiptCompletedAtUtc); // projection performed no write
    }

    // ── L/M/N/O: the operation-invoice obligation reuses the single financial rulebook ──

    [Fact]
    public void L_pending_operation_invoice_blocks()
    {
        var group = SatisfiedGroup(
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.PendingValidation);

        var p = Project(group);

        Assert.False(p.OperationInvoiceSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.OperationInvoicePending, p.BlockingReasons);
    }

    [Fact]
    public void M_satisfied_operation_invoice_passes()
    {
        var p = Project(SatisfiedGroup());

        Assert.True(p.OperationInvoiceSatisfied);
    }

    [Fact]
    public void N_short_close_satisfied_group_is_complete_and_flagged_closed_short()
    {
        // An approved short-close re-derives the aggregate to SATISFIED (Phase 3); the projector
        // consumes that reading and carries the ClosedShort marker as evidence.
        var group = SatisfiedGroup();

        var p = Project(group, hasApprovedShortClose: true);

        Assert.True(p.ClosedShort);
        Assert.True(p.OperationInvoiceSatisfied);
        Assert.True(p.Complete);
    }

    [Fact]
    public void O_accepted_divergence_over_coverage_reads_satisfied()
    {
        // Coverage above expected exists only through explicit Finance acceptance (Phase 3
        // validation-gate invariant); the aggregate is SATISFIED and the projection agrees.
        var group = SatisfiedGroup(
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied);

        var p = Project(group);

        Assert.True(p.OperationInvoiceSatisfied);
        Assert.True(p.Complete);
    }

    [Fact]
    public void O2_not_required_operation_invoice_is_satisfied_too()
    {
        var group = SatisfiedGroup(
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.NotRequired);

        Assert.True(Project(group).OperationInvoiceSatisfied);
    }

    // ── P/Q/R: the conditional fiscal receipt ──

    [Fact]
    public void P_required_missing_fiscal_receipt_makes_the_group_ready_not_complete()
    {
        var group = SatisfiedGroup(fiscalReceiptUploaded: false);

        var p = Project(group);

        Assert.True(p.FiscalReceiptRequired);
        Assert.False(p.FiscalReceiptSatisfied);
        Assert.False(p.Complete);
        Assert.True(p.ReadyForFiscalReceipt);
        Assert.Equal(
            new[] { GroupCompletionBlockingReasons.FiscalReceiptPending },
            p.BlockingReasons);
    }

    [Fact]
    public void Q_required_present_fiscal_receipt_completes()
    {
        var p = Project(SatisfiedGroup(fiscalReceiptUploaded: true));

        Assert.True(p.FiscalReceiptSatisfied);
        Assert.True(p.Complete);
        Assert.False(p.ReadyForFiscalReceipt);
    }

    [Fact]
    public void Q2_timestamp_without_attachment_id_does_not_satisfy_a_required_receipt()
    {
        // Without the attachment there is no stable GROUP_COMPLETED identity.
        var group = SatisfiedGroup(fiscalReceiptUploaded: false);
        group.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
        group.FiscalReceiptAttachmentId = null;

        var p = Project(group);

        Assert.False(p.FiscalReceiptSatisfied);
        Assert.False(p.Complete);
    }

    [Fact]
    public void R_no_separate_fiscal_receipt_owed_is_satisfied_without_any_attachment()
    {
        var group = SatisfiedGroup(separateFiscalReceipt: false);

        var p = Project(group);

        Assert.False(p.FiscalReceiptRequired);
        Assert.True(p.FiscalReceiptSatisfied);
        Assert.True(p.Complete);
        Assert.False(p.ReadyForFiscalReceipt);
        Assert.Null(group.FiscalReceiptAttachmentId);
    }

    // ── S: everything satisfied ──

    [Fact]
    public void S_fully_satisfied_group_is_complete_with_no_blocking_reasons()
    {
        var p = Project(SatisfiedGroup());

        Assert.True(p.Classified);
        Assert.True(p.PoSatisfied);
        Assert.True(p.NoBlockingCorrection);
        Assert.True(p.PaymentSatisfied);
        Assert.True(p.ReceiptSatisfied);
        Assert.True(p.OperationInvoiceSatisfied);
        Assert.True(p.FiscalReceiptSatisfied);
        Assert.True(p.Complete);
        Assert.Empty(p.BlockingReasons);
    }

    // ── Sibling-group isolation ──

    [Fact]
    public void Payments_of_a_sibling_group_never_block_this_group()
    {
        var group = SatisfiedGroup();
        var payments = new[]
        {
            Payment(Guid.NewGuid(), RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Planned)
        };

        var p = Project(group, payments);

        Assert.True(p.PaymentSatisfied);
        Assert.True(p.Complete);
    }

    // ── v2.229.2: authoritative payment evidence (REQ-17/08/2026-232) ──
    // The real post-ConfirmAdvancePayment shape: group parked in ADVANCE_PAYMENT_COMPLETED
    // (never a ladder status), payment proven exclusively by COMPLETED owed-money rows.

    private static RequestPayment CompletedPayment(Guid? groupId, string type, decimal actualPaid) => new()
    {
        RequestPoGroupId = groupId,
        PaymentType = type,
        PaymentStatus = RequestPayment.PaymentStatuses.Completed,
        PlannedAmount = actualPaid,
        ActualPaidAmount = actualPaid,
        CurrencyCode = "AOA"
    };

    private static RequestPoGroup FullAdvanceGroup(
        decimal totalAmount = 100_000m,
        bool requiresAdvanceRegularization = false)
    {
        var group = SatisfiedGroup(
            status: RequestConstants.PoGroupStatuses.AdvancePaymentCompleted,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.PendingUpload,
            fiscalReceiptUploaded: false,
            receiptStamped: false,
            requiresAdvanceRegularization: requiresAdvanceRegularization,
            itemStatuses: Pending);
        group.TotalAmount = totalAmount;
        return group;
    }

    [Fact]
    public void Req232_full_advance_satisfies_payment_but_not_the_group()
    {
        // EXACT production shape: TotalAmount 100.000, ADVANCE_PAYMENT_COMPLETED, one COMPLETED
        // ADVANCE row of 100.000, no PLANNED/SCHEDULED rows, no reconciliation, receipt and
        // Final Invoice pending, separate fiscal receipt owed.
        var group = FullAdvanceGroup();
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 100_000m) };

        var p = Project(group, payments);

        Assert.True(p.PaymentSatisfied);
        Assert.False(p.Complete);
        var codes = p.BlockingReasons.ToList();
        Assert.DoesNotContain(GroupCompletionBlockingReasons.PaymentPending, codes);
        Assert.Contains(GroupCompletionBlockingReasons.ReceiptPending, codes);
        Assert.Contains(GroupCompletionBlockingReasons.OperationInvoicePending, codes);
        Assert.Contains(GroupCompletionBlockingReasons.FiscalReceiptPending, codes);
    }

    [Fact]
    public void Partial_advance_stays_payment_pending_even_before_the_final_balance_row_exists()
    {
        // CRITICAL: 30% paid, and the 70% FINAL_BALANCE row is not created until reconciliation —
        // the absence of a PLANNED row must never read as fully paid.
        var group = FullAdvanceGroup();
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 30_000m) };

        var p = Project(group, payments);

        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending,
            p.BlockingReasons);
    }

    [Fact]
    public void Planned_final_balance_keeps_a_partial_advance_blocked()
    {
        var group = FullAdvanceGroup();
        var payments = new[]
        {
            CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 30_000m),
            Payment(group.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Planned)
        };

        Assert.False(Project(group, payments).PaymentSatisfied);
    }

    [Fact]
    public void Completed_final_balance_completes_the_payment_evidence()
    {
        var group = FullAdvanceGroup();
        var payments = new[]
        {
            CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 30_000m),
            CompletedPayment(group.Id, RequestPayment.PaymentTypes.FinalBalance, 70_000m)
        };

        var p = Project(group, payments);

        Assert.True(p.PaymentSatisfied);
        Assert.DoesNotContain(GroupCompletionBlockingReasons.PaymentPending,
            p.BlockingReasons);
    }

    [Fact]
    public void Paid_in_full_never_overrides_an_active_reconciliation()
    {
        var group = FullAdvanceGroup();
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 100_000m) };
        var reconciliations = new[]
        {
            Reconciliation(null, RequestReconciliation.ReconciliationStatuses.InProgress)
        };

        Assert.False(Project(group, payments, reconciliations).PaymentSatisfied);
    }

    [Fact]
    public void Paid_in_full_never_overrides_a_required_regularization()
    {
        var group = FullAdvanceGroup(requiresAdvanceRegularization: true);
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 100_000m) };

        Assert.False(Project(group, payments).PaymentSatisfied);

        var discharged = Project(group, payments, new[]
        {
            Reconciliation(group.Id, RequestReconciliation.ReconciliationStatuses.Completed)
        });
        Assert.True(discharged.PaymentSatisfied);
    }

    [Fact]
    public void Overpayment_satisfies_without_requiring_exact_equality()
    {
        var group = FullAdvanceGroup();
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 103_000m) };

        Assert.True(Project(group, payments).PaymentSatisfied);
    }

    [Fact]
    public void Request_level_completed_rows_are_never_counted_as_this_groups_money()
    {
        // Asymmetric by design: a null-group row BLOCKS every group when pending, but is never
        // SUMMED into any group's paid evidence — attributing it would fail open on multi-group
        // requests. This group therefore stays pending (its ladder status is not a paid stage).
        var group = FullAdvanceGroup();
        var payments = new[] { CompletedPayment(null, RequestPayment.PaymentTypes.Advance, 100_000m) };

        Assert.False(Project(group, payments).PaymentSatisfied);
    }

    [Fact]
    public void Partial_evidence_overrides_the_ladder_even_at_a_paid_stage_status()
    {
        // v2.229.3: a partially paid advance group that reached WAITING_RECEIPT through
        // receiving (delivery precedes the final balance by design) must NOT read as paid just
        // because WAITING_RECEIPT sits in the ladder — the 30.000-of-100.000 evidence wins.
        var group = FullAdvanceGroup();
        group.Status = RequestConstants.PoGroupStatuses.WaitingReceipt;
        var payments = new[] { CompletedPayment(group.Id, RequestPayment.PaymentTypes.Advance, 30_000m) };

        var p = Project(group, payments);

        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending, p.BlockingReasons);
    }

    [Fact]
    public void Ladder_still_covers_legacy_groups_without_any_evidence_rows()
    {
        // No payment rows at all: the ladder remains the only truth (its original purpose) —
        // a WAITING_RECEIPT group with zero evidence keeps reading as paid.
        var group = FullAdvanceGroup();
        group.Status = RequestConstants.PoGroupStatuses.WaitingReceipt;

        Assert.True(Project(group).PaymentSatisfied);
    }

    [Fact]
    public void Refunds_and_cancelled_rows_never_count_toward_paid_evidence()
    {
        var group = FullAdvanceGroup();
        var payments = new[]
        {
            CompletedPayment(group.Id, RequestPayment.PaymentTypes.Refund, 100_000m),
            new RequestPayment
            {
                RequestPoGroupId = group.Id,
                PaymentType = RequestPayment.PaymentTypes.Advance,
                PaymentStatus = RequestPayment.PaymentStatuses.Cancelled,
                PlannedAmount = 100_000m,
                ActualPaidAmount = 100_000m,
                CurrencyCode = "AOA"
            }
        };

        Assert.False(Project(group, payments).PaymentSatisfied);
    }

    // ── Idempotency key shapes (approved identity rule) ──

    [Fact]
    public void Group_completed_keys_use_the_attachment_or_the_nofr_literal()
    {
        var groupId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        Assert.Equal(
            $"GC:{groupId.ToString("D").ToLowerInvariant()}:{attachmentId.ToString("D").ToLowerInvariant()}",
            PostPaymentIdempotencyKeys.GroupCompleted(groupId, attachmentId));

        Assert.Equal(
            $"GC:{groupId.ToString("D").ToLowerInvariant()}:NOFR",
            PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(groupId));

        Assert.Equal(
            $"FR_UNLOCK:{groupId.ToString("D").ToLowerInvariant()}",
            PostPaymentIdempotencyKeys.FiscalReceiptUnlocked(groupId));

        // Never an empty identity.
        Assert.Throws<ArgumentException>(() =>
            PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(Guid.Empty));
    }

    // ── v2.229.5: the receiving record lives on EITHER side of the award pointer ──
    // (REQ-17/08/2026-232, fifth STATE 1 finding.) The batch/candidate QUOTATION model keeps
    // SelectedQuotationItemId as a compatibility pointer while the receiving UI registers on
    // the RequestLineItem; the legacy QUOTATION flow registers on the winning QuotationItem;
    // PAYMENT has no pointer at all. The rulebook accepts whichever record proves full receipt.

    /// <summary>A group line item carrying the batch-model compatibility pointer, with the
    /// receiving record independently controllable on each side of it.</summary>
    private static RequestLineItem AwardedItem(
        RequestPoGroup group,
        LineItemStatus? ownStatus,
        LineItemStatus? quotationItemStatus,
        decimal quantity = 1m,
        decimal receivedQuantity = 0m,
        bool isDeleted = false)
    {
        var quotationItemId = Guid.NewGuid();
        return new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = group.RequestId,
            RequestPoGroupId = group.Id,
            LineNumber = group.LineItems.Count + 1,
            Description = "ZZTEST awarded item",
            Quantity = quantity,
            ReceivedQuantity = receivedQuantity,
            IsDeleted = isDeleted,
            LineItemStatusId = ownStatus?.Id,
            LineItemStatus = ownStatus,
            SelectedQuotationItemId = quotationItemId,
            SelectedQuotationItem = new QuotationItem
            {
                Id = quotationItemId,
                LineNumber = 1,
                Description = "ZZTEST winning quotation item",
                Quantity = quantity,
                LineItemStatusId = quotationItemStatus?.Id,
                LineItemStatus = quotationItemStatus
            }
        };
    }

    [Fact]
    public void R5A_empty_group_and_all_deleted_items_both_fail_closed()
    {
        var empty = SatisfiedGroup(receiptStamped: false);
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(empty));
        Assert.False(Project(empty).ReceiptSatisfied);

        // Every item soft-deleted: All() over the filtered-empty set must never read true.
        var deletedOnly = SatisfiedGroup(receiptStamped: false);
        deletedOnly.LineItems.Add(AwardedItem(deletedOnly, Received, Received, isDeleted: true));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(deletedOnly));
        Assert.False(Project(deletedOnly).ReceiptSatisfied);
    }

    [Fact]
    public void R5B_legacy_payment_shape_reads_the_own_line_item_record()
    {
        // No pointer: own RECEIVED proves it, own PENDING/PARTIAL blocks it (pre-existing shape).
        var received = SatisfiedGroup(receiptStamped: false, itemStatuses: Received);
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(received));

        var pending = SatisfiedGroup(receiptStamped: false, itemStatuses: Pending);
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(pending));

        var partial = SatisfiedGroup(receiptStamped: false, itemStatuses: Partially);
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(partial));
    }

    [Fact]
    public void R5C_legacy_quotation_shape_reads_the_winning_quotation_item_record()
    {
        // Pointer set, receipt registered on the QuotationItem, own line item never touched:
        // the old quotation flow keeps working through the second disjunct.
        var group = SatisfiedGroup(receiptStamped: false);
        group.LineItems.Add(AwardedItem(group, ownStatus: Pending, quotationItemStatus: Received,
            quantity: 1m, receivedQuantity: 0m));

        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
        Assert.True(Project(group).ReceiptSatisfied);
    }

    [Fact]
    public void R5D_req232_batch_shape_line_item_record_proves_the_receipt()
    {
        // THE regression: pointer set (batch award), receiving registered on the RequestLineItem
        // (own status RECEIVED at full quantity), winning QuotationItem still PENDING. Before
        // v2.229.5 the ternary read only the quotation side and this exact shape evaluated false.
        var group = SatisfiedGroup(receiptStamped: false);
        group.LineItems.Add(AwardedItem(group, ownStatus: Received, quotationItemStatus: Pending,
            quantity: 1m, receivedQuantity: 1m));

        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
    }

    [Fact]
    public void R5E_projector_reads_receipt_satisfied_from_the_batch_shape_without_a_stamp()
    {
        // §11 of the instruction: even before the live stamp exists, the pure projector must
        // read ReceiptSatisfied=true from the corrected shared rule.
        var group = SatisfiedGroup(receiptStamped: false, fiscalReceiptUploaded: false,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.PendingUpload);
        group.LineItems.Add(AwardedItem(group, ownStatus: Received, quotationItemStatus: Pending,
            quantity: 1m, receivedQuantity: 1m));

        var p = Project(group);

        Assert.True(p.ReceiptSatisfied);
        Assert.Null(group.OperationalReceiptCompletedAtUtc); // pure read, no write
        Assert.DoesNotContain(GroupCompletionBlockingReasons.ReceiptPending, p.BlockingReasons);
        Assert.False(p.Complete); // invoice + fiscal receipt still honestly pending
    }

    [Fact]
    public void R5F_partial_batch_shape_stays_blocked_on_both_sides()
    {
        // 2 authorized, 1 received: own PARTIALLY_RECEIVED, quotation side PENDING — the OR of
        // two non-received records is still not received. Partial safety is untouched.
        var group = SatisfiedGroup(receiptStamped: false);
        group.LineItems.Add(AwardedItem(group, ownStatus: Partially, quotationItemStatus: Pending,
            quantity: 2m, receivedQuantity: 1m));

        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
        var p = Project(group);
        Assert.False(p.ReceiptSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.ReceiptPending, p.BlockingReasons);
    }

    [Fact]
    public void R5G_one_received_sibling_never_carries_an_unreceived_one()
    {
        // Mixed group: the batch-shape item is fully received but its sibling is not — the
        // group-level answer stays false regardless of which side proves the received one.
        var group = SatisfiedGroup(receiptStamped: false);
        group.LineItems.Add(AwardedItem(group, ownStatus: Received, quotationItemStatus: Pending,
            quantity: 1m, receivedQuantity: 1m));
        group.LineItems.Add(AwardedItem(group, ownStatus: Pending, quotationItemStatus: Pending,
            quantity: 3m, receivedQuantity: 0m));

        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
    }
}
