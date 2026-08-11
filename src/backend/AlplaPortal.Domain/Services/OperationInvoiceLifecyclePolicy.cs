using System;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Release 4 Phase 2: when an operation invoice may be created, edited, validated, voided or
/// replaced — the document lifecycle, distinct from the group obligation aggregate
/// (<see cref="RequestConstants.OperationInvoiceStatuses"/>).
///
/// <para>Pure and side-effect-free, and deliberately NOT built on
/// <see cref="RequestConstants.OperationInvoiceDocumentStatuses.IsEditable"/>: that helper answers
/// "is it not yet validated", which wrongly reads terminal documents as editable. This policy is
/// the explicit answer per operation.</para>
///
/// <para>The approved rules it encodes: manual creation lands in PENDING_VALIDATION; editing stops
/// at validation; VALIDATED is immutable and corrected only by replacement; void exists only
/// before validation; VOIDED/REJECTED/REPLACEMENT_REQUESTED are terminal and non-effective — they
/// contribute nothing and do not occupy a document identity for duplicate purposes.</para>
/// </summary>
public static class OperationInvoiceLifecyclePolicy
{
    /// <summary>Manual Phase 2 creation is complete on arrival — straight to Finance's queue.
    /// UPLOADED stays reserved for the future OCR intake, whose reading is still pending.</summary>
    public const string InitialManualStatus =
        RequestConstants.OperationInvoiceDocumentStatuses.PendingValidation;

    /// <summary>
    /// Request statuses in which an operation invoice may be registered (approved Phase 2b list):
    /// the entire post-approval window, explicitly INCLUDING after payment — a PROFORMA/advance
    /// case is regularized by a final invoice that arrives once the money has already moved.
    /// DRAFT, the approval/adjustment stages, REJECTED, CANCELLED and COMPLETED stay closed;
    /// reopening a completed request is a future Finance mechanism, not a Phase 2 side door.
    /// </summary>
    public static readonly string[] CreateAllowedRequestStatuses =
    {
        // "WAITING_PO" exists only as a GROUP status: while groups await their P.O., the REQUEST
        // sits in APPROVED (then PO_PARTIALLY_UPLOADED) — so that window is covered by these two.
        RequestConstants.Statuses.FinalApproved,            // "APPROVED"
        RequestConstants.Statuses.PoPartiallyUploaded,
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.Paid,
        RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.WaitingSupplierDelivery,
        RequestConstants.Statuses.WaitingReceipt,
        RequestConstants.Statuses.WaitingReconciliation,
        RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.WaitingFiscalReceipt,
        RequestConstants.Statuses.AdvancePaymentRequired,
        RequestConstants.Statuses.AdvancePaymentScheduled,
        RequestConstants.Statuses.AdvancePaymentCompleted
    };

    public static bool CanCreateInRequestStatus(string? requestStatusCode) =>
        CreateAllowedRequestStatuses.Any(s =>
            string.Equals(s, requestStatusCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Update/Void/Replace share the create window (approved Phase 2c rule): the same statuses
    /// that may receive an invoice may correct one, WAITING_PO_CORRECTION stays blocked for every
    /// mutation, and reading is never gated by status at all.
    /// </summary>
    public static bool CanMutateInRequestStatus(string? requestStatusCode) =>
        CanCreateInRequestStatus(requestStatusCode);

    /// <summary>The only document statuses in which fields may still change.</summary>
    public static readonly string[] EditableStatuses =
    {
        RequestConstants.OperationInvoiceDocumentStatuses.Uploaded,
        RequestConstants.OperationInvoiceDocumentStatuses.PendingValidation
    };

    public static bool IsEditable(string? documentStatus) =>
        EditableStatuses.Any(s => string.Equals(s, documentStatus, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finance validation acts only on a document sitting in its queue.</summary>
    public static bool CanValidate(string? documentStatus) =>
        string.Equals(documentStatus,
            RequestConstants.OperationInvoiceDocumentStatuses.PendingValidation,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Void exists only before validation (approved rule #8): a VALIDATED invoice is immutable
    /// and corrected exclusively by replacement.
    /// </summary>
    public static bool CanVoid(string? documentStatus) => IsEditable(documentStatus);

    /// <summary>Replacement is the one path out of VALIDATED. Pre-validation, edit or void instead.</summary>
    public static bool CanReplace(string? documentStatus) =>
        string.Equals(documentStatus,
            RequestConstants.OperationInvoiceDocumentStatuses.Validated,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Terminal, non-effective statuses: they no longer represent a recognized debt, so they are
    /// EXCLUDED from duplicate detection — a corrected reissue with the same supplier + number +
    /// series must be representable. Mirrors
    /// <see cref="RequestConstants.OperationInvoiceDocumentStatuses.Terminal"/>.
    /// </summary>
    public static bool IsEffectiveForDuplicateCheck(string? documentStatus) =>
        !RequestConstants.OperationInvoiceDocumentStatuses.Terminal.Any(s =>
            string.Equals(s, documentStatus, StringComparison.OrdinalIgnoreCase));
}
