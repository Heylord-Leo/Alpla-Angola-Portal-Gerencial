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
/// </summary>
public static class FiscalReceiptStateDeriver
{
    /// <summary>
    /// Returns <see cref="RequestConstants.FiscalReceiptStatuses"/>: UPLOADED, PENDING or LOCKED.
    /// </summary>
    public static string Derive(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.FiscalReceiptUploadedAtUtc != null)
            return RequestConstants.FiscalReceiptStatuses.Uploaded;

        var receiptDone = group.OperationalReceiptCompletedAtUtc != null;
        var invoiceDone = RequestConstants.OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus);

        return receiptDone && invoiceDone
            ? RequestConstants.FiscalReceiptStatuses.PendingUpload
            : RequestConstants.FiscalReceiptStatuses.Locked;
    }

    /// <summary>
    /// True when Finance may upload the Fiscal Receipt for this group right now.
    /// Callers must still enforce the Finance role and the group's non-terminal status.
    /// </summary>
    public static bool CanUploadFiscalReceipt(RequestPoGroup group)
        => Derive(group) == RequestConstants.FiscalReceiptStatuses.PendingUpload;

    /// <summary>
    /// True when all three dimensions are satisfied and the group is eligible for completion.
    /// Requires a non-null <c>FiscalReceiptAttachmentId</c>: without it there is no stable
    /// GROUP_COMPLETED identity, so completion must not proceed.
    /// </summary>
    public static bool IsGroupCompletable(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group.OperationalReceiptCompletedAtUtc != null
            && RequestConstants.OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus)
            && group.FiscalReceiptUploadedAtUtc != null
            && group.FiscalReceiptAttachmentId != null
            && group.FiscalReceiptAttachmentId != Guid.Empty;
    }
}
