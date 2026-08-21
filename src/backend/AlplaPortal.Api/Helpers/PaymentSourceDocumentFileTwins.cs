using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Helpers;

/// <summary>The live request whose commercial source file matches the candidate's hash.</summary>
public sealed record ActiveFileTwin(Request Request, Guid? PaymentSourceDocumentId, bool IsLegacySourceAttachment);

/// <summary>
/// The ONE discrimination behind the cross-request LEVEL 1 file rule (v2.229.10): is this file
/// hash already in use as the COMMERCIAL SOURCE FILE of a LIVE request?
///
/// <para>Two shapes qualify (MODEL B of the cross-request file audit): an active (non-voided)
/// <see cref="PaymentSourceDocument"/>, and — for requests created before the
/// PaymentSourceDocument model existed (table born 2026-08-04; e.g. REQ-21/07/2026-116) — a
/// source-typed legacy attachment (<c>PROFORMA</c> / <c>PAYMENT_SOURCE_DOCUMENT</c>) that has no
/// document row at all. An attachment WITH a document row is judged by that row alone, so a
/// voided document stays inactive evidence. Generic supporting attachments (receipts, POs,
/// payment proofs, quotation files) never qualify — they legitimately recur across requests and
/// stay on the warn tier.</para>
///
/// <para>Shared by the authoritative persistence guard
/// (<c>PaymentSourceDocumentsController.GuardCrossRequestFileTwinAsync</c>), the request-scoped
/// preflight, and the generic <c>attachments/check-duplicate</c> endpoint the creation wizard
/// consults before any request exists — one query, three callers, so the preflight
/// classification and the persistence enforcement cannot drift.</para>
/// </summary>
public static class PaymentSourceDocumentFileTwins
{
    /// <summary>
    /// Requests whose source documents no longer guard against double payment. CANCELLED and
    /// REJECTED requests can never lead to a payment, so a document registered on one is not a
    /// debt in flight — blocking on it would be false double-payment protection.
    /// </summary>
    public static readonly string[] TerminalDeadRequestStatuses =
        { RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected };

    /// <summary>
    /// Attachment types that carry a request's commercial origin in the pre-Release-3 model.
    /// Deliberately narrow: everything else on RequestAttachment is supporting evidence.
    /// </summary>
    public static readonly string[] LegacySourceAttachmentTypes =
        { RequestAttachment.TYPE_PROFORMA, RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT };

    /// <summary>
    /// The identical file (by hash) in use as the commercial source file of a live request, or
    /// null. <paramref name="excludeRequestId"/> keeps a request from matching its own documents;
    /// pass null to search everywhere (the wizard has no request yet).
    /// </summary>
    public static async Task<ActiveFileTwin?> FindActiveTwinAsync(
        ApplicationDbContext context, string? fileHash, Guid? excludeRequestId)
    {
        if (string.IsNullOrWhiteSpace(fileHash)) return null;

        // ── Shape 1: an active PaymentSourceDocument (Release 3 model) ──
        var documentTwin = await context.PaymentSourceDocuments
            .Where(d => !d.IsVoided && (excludeRequestId == null || d.RequestId != excludeRequestId))
            .Join(context.RequestAttachments.Where(a => !a.IsDeleted && a.FileHash == fileHash),
                  d => d.AttachmentId, a => a.Id, (d, a) => d)
            .Join(context.Requests,
                  d => d.RequestId, r => r.Id, (d, r) => new { d, r })
            .Where(x => x.r.Status == null || !TerminalDeadRequestStatuses.Contains(x.r.Status.Code))
            .FirstOrDefaultAsync();

        if (documentTwin != null)
            return new ActiveFileTwin(documentTwin.r, documentTwin.d.Id, IsLegacySourceAttachment: false);

        // ── Shape 2: a source-typed legacy attachment with NO document row ──
        // The document row, when one exists, is the authority (voided ⇒ inactive evidence), so
        // this branch only ever matches attachments the Release 3 model never touched.
        var legacyTwin = await context.RequestAttachments
            .Where(a => !a.IsDeleted && a.FileHash == fileHash
                        && LegacySourceAttachmentTypes.Contains(a.AttachmentTypeCode)
                        && (excludeRequestId == null || a.RequestId != excludeRequestId)
                        && !context.PaymentSourceDocuments.Any(d => d.AttachmentId == a.Id))
            .Join(context.Requests,
                  a => a.RequestId, r => r.Id, (a, r) => new { a, r })
            .Where(x => x.r.Status == null || !TerminalDeadRequestStatuses.Contains(x.r.Status.Code))
            .FirstOrDefaultAsync();

        return legacyTwin == null
            ? null
            : new ActiveFileTwin(legacyTwin.r, null, IsLegacySourceAttachment: true);
    }
}
