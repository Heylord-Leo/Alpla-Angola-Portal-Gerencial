using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Human-readable summary (pt-PT) of what still blocks a PO group from completing, derived from
/// the three independent post-payment dimensions. Pure and side-effect-free.
///
/// A LOCKED Fiscal Receipt is deliberately NOT listed: it is not actionable yet, and listing it
/// would suggest work that nobody can perform.
/// </summary>
public static class PostPaymentPendingReason
{
    public const string Completed = "Concluído";
    public const string OperationalReceipt = "Recebimento Operacional";
    public const string ClassificationPending = "Classificação Pendente";
    public const string FinalInvoice = "Fatura Final";
    public const string FiscalReceipt = "Recibo Fiscal";

    private const string Separator = " · ";

    /// <summary>
    /// Returns the pending dimensions joined by "·", or "Concluído" when nothing is outstanding.
    /// </summary>
    public static string Compute(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var reasons = new List<string>(3);

        if (group.OperationalReceiptCompletedAtUtc == null)
            reasons.Add(OperationalReceipt);

        if (RequestConstants.FinalInvoiceStatuses.IsBlocking(group.FinalInvoiceStatus))
        {
            reasons.Add(string.Equals(
                    group.FinalInvoiceStatus,
                    RequestConstants.FinalInvoiceStatuses.Unclassified,
                    StringComparison.OrdinalIgnoreCase)
                ? ClassificationPending
                : FinalInvoice);
        }

        // Only the actionable state is reported. LOCKED means another dimension is already
        // listed above; UPLOADED means this dimension is done.
        if (FiscalReceiptStateDeriver.Derive(group) == RequestConstants.FiscalReceiptStatuses.PendingUpload)
            reasons.Add(FiscalReceipt);

        return reasons.Count == 0 ? Completed : string.Join(Separator, reasons);
    }
}
