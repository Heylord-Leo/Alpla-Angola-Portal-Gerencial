using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.4 — which P.O. file is THE current document of a group. One rule for every reader (request page, Workspace,
/// Finance views, Print View, the correction modal), so no consumer ever picks a superseded file.
///
/// <para>Order of precedence: the pointer stamped by RegisterPo (<c>RequestPoGroup.CurrentPoAttachmentId</c>) when it still
/// names a live file; otherwise — groups registered before this version — the newest P.O. file that is neither deleted,
/// voided nor superseded; otherwise the newest non-deleted, non-voided file; otherwise null. Deleted and voided files are
/// never current. Pure.</para>
/// </summary>
public static class PoDocumentSelection
{
    public sealed record Candidate(Guid Id, Guid? RequestPoGroupId, string? AttachmentTypeCode, DateTime UploadedAtUtc, bool IsDeleted, DateTime? VoidedAtUtc, DateTime? SupersededAtUtc);

    public const string PurchaseOrderTypeCode = "PO";

    /// <summary>
    /// The current P.O. document of <paramref name="groupId"/>. Candidates of other groups (or without a group) are ignored
    /// here as well, so no caller can select a document across group boundaries; a pointer naming a file of another group,
    /// a deleted or a voided file is ignored and the fallback applies.
    /// </summary>
    public static Guid? ResolveCurrentPoAttachmentId(Guid groupId, Guid? pointer, IEnumerable<Candidate> attachments)
    {
        var live = attachments
            .Where(a => a.RequestPoGroupId == groupId && string.Equals(a.AttachmentTypeCode, PurchaseOrderTypeCode, StringComparison.OrdinalIgnoreCase) && !a.IsDeleted && a.VoidedAtUtc == null)
            .OrderByDescending(a => a.UploadedAtUtc).ThenByDescending(a => a.Id)
            .ToList();
        if (live.Count == 0) return null;
        if (pointer.HasValue && live.Any(a => a.Id == pointer.Value)) return pointer;
        return (live.FirstOrDefault(a => a.SupersededAtUtc == null) ?? live[0]).Id;
    }
}
