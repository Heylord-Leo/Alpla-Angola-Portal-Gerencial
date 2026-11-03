using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.5 — which P.O. files are REGISTERED EVIDENCE and therefore can never be deleted (soft or hard), whatever the request
/// or group status says. Found in TEST on REQ-28/10/2026-628: the correction wizard offered "Remover P.O atual" and the delete
/// endpoint accepted it while the group was WAITING_PO_CORRECTION, so the P.O. file registered the day before was soft-deleted
/// (row kept with IsDeleted=1, bytes kept on disk, but hidden from every list and from the download endpoint) before the next
/// correction superseded it. Replacement is supersession (RegisterPo), never deletion.
///
/// <para>Protected = any of: voided (DEC-150 annulment evidence), superseded (replaced by a correction), the group's current
/// document (pointer), or — legacy groups registered before the pointer existed — a REGISTER_PO / REREGISTER_PO of the owning
/// group recorded at or after the file's upload (the file was on the group when it was registered; registrations whose group
/// cannot be told from their history comment count for every group of the request — protective by default). A P.O. file
/// uploaded after the last registration of its group and never pointed at is a temporary, unregistered upload: deletable
/// (wizard clean-up of an unfinished attempt). Pure.</para>
/// </summary>
public static class PoEvidenceProtection
{
    public static class Reasons
    {
        public const string Voided = "PO_EVIDENCE_VOIDED";
        public const string Superseded = "PO_EVIDENCE_SUPERSEDED";
        public const string Current = "PO_EVIDENCE_CURRENT";
        public const string RegisteredAfterUpload = "PO_EVIDENCE_REGISTERED";
    }

    public const string ErrorCode = "PO_EVIDENCE_PROTECTED";

    /// <summary>The file facts the rule needs.</summary>
    public sealed record FileFacts(Guid Id, Guid? RequestPoGroupId, DateTime UploadedAtUtc, DateTime? VoidedAtUtc, DateTime? SupersededAtUtc);

    /// <summary>One REGISTER_PO / REREGISTER_PO history row: when, and the group it names (null when the comment names none).</summary>
    public sealed record Registration(DateTime AtUtc, Guid? GroupIdPrefixMatch);

    public sealed record Verdict(bool IsProtected, string? ReasonCode, string Detail);

    public static Verdict Evaluate(FileFacts file, Guid? groupCurrentPointer, IEnumerable<Registration> registrationsOfRequest)
    {
        if (file.VoidedAtUtc.HasValue)
            return new Verdict(true, Reasons.Voided, "Este documento de P.O. foi anulado (sem efeito) e faz parte da evidência histórica do grupo; não pode ser removido.");
        if (file.SupersededAtUtc.HasValue)
            return new Verdict(true, Reasons.Superseded, "Este documento de P.O. foi substituído por uma correção e é mantido como histórico; não pode ser removido.");
        if (groupCurrentPointer.HasValue && groupCurrentPointer.Value == file.Id)
            return new Verdict(true, Reasons.Current, "Este é o documento de P.O. atual do grupo (registado). Para o trocar, registe a correção com o novo ficheiro: o atual fica como histórico (substituído).");
        // Pointer-era group (registered at least once since v2.249.4): every registration made the chosen file the pointer and
        // superseded every other live file of the group, so a live file that is neither the pointer nor superseded nor voided
        // was uploaded AFTER the last registration — a temporary upload, deletable whatever the (older) registration history
        // says. The timestamp heuristic below is only for groups that never received a pointer.
        if (groupCurrentPointer.HasValue && file.RequestPoGroupId.HasValue)
            return new Verdict(false, null, string.Empty);
        // Legacy heuristic (no pointer). Tolerance of ONE second: a registration recorded up to 1 s BEFORE the upload stamp still
        // counts (the two stamps are taken by different statements of the same flow). Consequences: an exact timestamp tie is
        // protected; an upload within 1 s after a registration is protected (false positive, protective by design); an upload
        // more than 1 s after the LAST registration of its group is a temporary upload (deletable).
        var registeredAfter = registrationsOfRequest.Any(r => r.AtUtc >= file.UploadedAtUtc.AddSeconds(-1)
                                                             && (r.GroupIdPrefixMatch == null || !file.RequestPoGroupId.HasValue || r.GroupIdPrefixMatch == file.RequestPoGroupId));
        if (registeredAfter)
            return new Verdict(true, Reasons.RegisteredAfterUpload, "Este documento de P.O. estava no grupo quando a P.O. foi registada e faz parte da evidência histórica; não pode ser removido.");
        return new Verdict(false, null, string.Empty);
    }

    /// <summary>
    /// Reads the group a registration history row names. RegisterPo writes "[Grupo P.O.: … | GroupId: xxxxxxxx]" (first 8 hex of
    /// the group id); older rows carry no group → null (= counts for every group of the request).
    /// </summary>
    public static Guid? GroupFromHistoryComment(string? comment, IEnumerable<Guid> groupIdsOfRequest)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var marker = comment.IndexOf("GroupId: ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var prefix = new string(comment.Skip(marker + "GroupId: ".Length).TakeWhile(c => Uri.IsHexDigit(c) || c == '-').Take(36).ToArray()).ToLowerInvariant();
        if (prefix.Length < 8) return null;
        var match = groupIdsOfRequest.Where(g => g.ToString().StartsWith(prefix.Substring(0, Math.Min(prefix.Length, 36)), StringComparison.OrdinalIgnoreCase)).ToList();
        return match.Count == 1 ? match[0] : null;
    }

    public static readonly string[] RegistrationActions = { "REGISTER_PO", "REREGISTER_PO" };
}
