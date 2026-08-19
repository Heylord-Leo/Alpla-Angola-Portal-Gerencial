using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Helpers;

/// <summary>
/// The ONE discrimination behind the cross-request LEVEL 1 file rule (v2.229.10): is this file
/// hash registered as an ACTIVE source document of a LIVE request?
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
    /// The identical file (by hash) registered as an active (non-voided) source document of a
    /// live request, or null. <paramref name="excludeRequestId"/> keeps a request from matching
    /// its own documents; pass null to search everywhere (the wizard has no request yet).
    /// </summary>
    public static async Task<(PaymentSourceDocument Document, Request Request)?> FindActiveTwinAsync(
        ApplicationDbContext context, string? fileHash, Guid? excludeRequestId)
    {
        if (string.IsNullOrWhiteSpace(fileHash)) return null;

        var twin = await context.PaymentSourceDocuments
            .Where(d => !d.IsVoided && (excludeRequestId == null || d.RequestId != excludeRequestId))
            .Join(context.RequestAttachments.Where(a => !a.IsDeleted && a.FileHash == fileHash),
                  d => d.AttachmentId, a => a.Id, (d, a) => d)
            .Join(context.Requests,
                  d => d.RequestId, r => r.Id, (d, r) => new { d, r })
            .Where(x => x.r.Status == null || !TerminalDeadRequestStatuses.Contains(x.r.Status.Code))
            .FirstOrDefaultAsync();

        return twin == null ? null : (twin.d, twin.r);
    }
}
