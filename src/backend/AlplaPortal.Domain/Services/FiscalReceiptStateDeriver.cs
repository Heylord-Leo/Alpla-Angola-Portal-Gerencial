using System;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Derives the Fiscal Receipt state of a PO group. Pure and side-effect-free — the state is
/// NEVER persisted as a column, it is always computed from the two other dimensions plus the
/// receipt attachment itself.
///
/// The Fiscal Receipt is the terminal closing step (rule R5): it unlocks only once Operational
/// Receipt is done AND the Final Invoice obligation is satisfied. An UNCLASSIFIED group is
/// blocking, so it can never unlock (rule R15).
///
/// <para><b>Phase 4 approved amendment</b>: the dimension is CONDITIONAL on the persisted
/// classification result. A group with <c>RequiresSeparateFiscalReceipt = false</c> (a
/// Factura-Recibo already documents the payment) owes no separate receipt, reads
/// <see cref="RequestConstants.FiscalReceiptStatuses.NotRequired"/>, and must never be left
/// waiting for a document it does not owe.</para>
/// </summary>
public static class FiscalReceiptStateDeriver
{
    /// <summary>
    /// Returns <see cref="RequestConstants.FiscalReceiptStatuses"/>: UPLOADED, NOT_REQUIRED,
    /// PENDING or LOCKED.
    /// </summary>
    public static string Derive(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // An actually uploaded receipt is reported honestly regardless of obligation — the
        // upload guard, not this deriver, is what prevents unwanted states.
        if (group.FiscalReceiptUploadedAtUtc != null)
            return RequestConstants.FiscalReceiptStatuses.Uploaded;

        // Unclassified first: the obligation flags of an unclassified group are meaningless
        // (RequiresSeparateFiscalReceipt is still at its column default), so the dimension
        // stays LOCKED rather than reading a default as "not owed" (rule R15).
        var unclassified = string.Equals(
            group.OperationInvoiceStatus,
            RequestConstants.OperationInvoiceStatuses.Unclassified,
            StringComparison.OrdinalIgnoreCase);
        if (unclassified)
            return RequestConstants.FiscalReceiptStatuses.Locked;

        if (!group.RequiresSeparateFiscalReceipt)
            return RequestConstants.FiscalReceiptStatuses.NotRequired;

        var receiptDone = group.OperationalReceiptCompletedAtUtc != null;
        var invoiceDone = RequestConstants.OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus);

        return receiptDone && invoiceDone
            ? RequestConstants.FiscalReceiptStatuses.PendingUpload
            : RequestConstants.FiscalReceiptStatuses.Locked;
    }

    /// <summary>
    /// True when Finance may upload the Fiscal Receipt for this group right now. A NOT_REQUIRED
    /// group takes no upload — there is nothing owed to attach.
    /// Callers must still enforce the Finance role and the group's non-terminal status.
    /// </summary>
    public static bool CanUploadFiscalReceipt(RequestPoGroup group)
        => Derive(group) == RequestConstants.FiscalReceiptStatuses.PendingUpload;

    /// <summary>
    /// True when every applicable dimension is satisfied and the group is eligible for completion.
    ///
    /// <para>When a separate receipt is owed, a non-null <c>FiscalReceiptAttachmentId</c> is
    /// required: without it there is no stable GROUP_COMPLETED identity
    /// (<c>GC:{GroupId}:{FiscalReceiptAttachmentId}</c>), so completion must not proceed. When no
    /// separate receipt is owed, the dimension is satisfied by classification and the completion
    /// identity is the approved <c>GC:{GroupId}:NOFR</c> form instead.</para>
    /// </summary>
    public static bool IsGroupCompletable(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var fiscalReceiptSatisfied = !group.RequiresSeparateFiscalReceipt
            || (group.FiscalReceiptUploadedAtUtc != null
                && group.FiscalReceiptAttachmentId != null
                && group.FiscalReceiptAttachmentId != Guid.Empty);

        return group.OperationalReceiptCompletedAtUtc != null
            && RequestConstants.OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus)
            && fiscalReceiptSatisfied;
    }
}
