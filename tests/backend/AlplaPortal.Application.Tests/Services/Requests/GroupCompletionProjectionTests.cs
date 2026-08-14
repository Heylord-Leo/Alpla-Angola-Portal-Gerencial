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
}
