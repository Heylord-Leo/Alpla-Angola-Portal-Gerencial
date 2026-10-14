using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>Facts needed to decide whether a request is in the legacy request-level Finance-return shape.</summary>
public sealed record LegacyFinanceReturnFacts(
    Guid RequestId,
    string? ScalarStatusCode,
    IReadOnlyList<LegacyFinanceReturnGroupFact> LiveGroups,
    DateTime? LatestFinanceReturnUtc,
    DateTime? LatestPoRegistrationUtc,
    int LivePaymentCount);

public sealed record LegacyFinanceReturnGroupFact(Guid GroupId, string Status);

public sealed record LegacyFinanceReturnVerdict(bool Qualifies, Guid? GroupId, string Reason);

/// <summary>Whether financial actions (schedule / pay / advance) are blocked by an unresolved request-level return.</summary>
public sealed record LegacyFinanceReturnBlock(bool Blocked, string Reason);

/// <summary>
/// Pure predicate for the legacy Finance-return shape (docs/APPROVAL_NOTIFICATIONS_PHASES.md §G.19): before the
/// group-scoped return (cc85ec7, 2026-08-23) `ReturnForAdjustment` set only the request scalar to
/// WAITING_PO_CORRECTION and left the group at PO_ISSUED. A request qualifies ONLY when every fact is present and
/// consistent: scalar WAITING_PO_CORRECTION; exactly one non-cancelled group and it is PO_ISSUED; a
/// FINANCE_RETURN_ADJUSTMENT row exists and is later than the latest REGISTER_PO/REREGISTER_PO row (which must exist);
/// no non-cancelled payment row. Missing or ambiguous evidence never qualifies. A current-code single-group correction
/// (group WAITING_PO_CORRECTION) and every multi-group shape (e.g. REQ-275: scalar PO_PARTIALLY_UPLOADED with one
/// returned group among others) are outside the predicate by construction.
/// </summary>
public static class LegacyFinanceReturnEvaluator
{
    public const string ReasonCode = "LEGACY_RETURN_PENDING_CORRECTION";
    public const string FinanceReturnAction = "FINANCE_RETURN_ADJUSTMENT";
    public static readonly string[] PoRegistrationActions = { "REGISTER_PO", "REREGISTER_PO" };

    public static LegacyFinanceReturnVerdict Evaluate(LegacyFinanceReturnFacts f)
    {
        if (!string.Equals(f.ScalarStatusCode, RequestConstants.Statuses.WaitingPoCorrection, StringComparison.Ordinal))
            return No($"estado do pedido não é WAITING_PO_CORRECTION ({f.ScalarStatusCode ?? "?"})");
        if (f.LiveGroups.Count == 0) return No("sem grupo P.O. ativo");
        if (f.LiveGroups.Count > 1) return No($"vários grupos P.O. ativos ({f.LiveGroups.Count}); forma multi-grupo fora do âmbito");
        var group = f.LiveGroups[0];
        if (!string.Equals(group.Status, RequestConstants.PoGroupStatuses.PoIssued, StringComparison.Ordinal))
            return No(string.Equals(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection, StringComparison.Ordinal)
                ? "grupo já em WAITING_PO_CORRECTION (devolução por grupo; nada a corrigir)"
                : $"grupo em {group.Status}, não PO_ISSUED");
        if (f.LatestFinanceReturnUtc == null) return No("sem FINANCE_RETURN_ADJUSTMENT no histórico");
        if (f.LatestPoRegistrationUtc == null) return No("sem REGISTER_PO/REREGISTER_PO no histórico (evidência insuficiente)");
        if (f.LatestPoRegistrationUtc.Value >= f.LatestFinanceReturnUtc.Value)
            return No("existe registo de P.O. posterior ou simultâneo à devolução (já re-registada)");
        if (f.LivePaymentCount > 0) return No($"existem {f.LivePaymentCount} pagamento(s) ativo(s)");
        return new LegacyFinanceReturnVerdict(true, group.GroupId, ReasonCode);
    }

    private static LegacyFinanceReturnVerdict No(string reason) => new(false, null, reason);

    /// <summary>
    /// Financial blocking decision — a DIFFERENT concern from repair eligibility. The scalar WAITING_PO_CORRECTION means
    /// Finance returned the request for P.O. correction. Under current code the returned group carries the status
    /// WAITING_PO_CORRECTION itself (group-scoped return) and the group guards already refuse it while siblings stay
    /// actionable. When the scalar says "returned" but NO live group carries the return, the return was request-level
    /// (legacy) and unresolved: nothing may be scheduled, paid or advanced on that request, whatever the history or
    /// payment rows look like — missing evidence never enables a financial action, and with several live groups the
    /// returned one cannot be known (no guessing).
    ///  - scalar ≠ WAITING_PO_CORRECTION (e.g. PO_PARTIALLY_UPLOADED, REQ-275 shape) → not blocked (group guards decide)
    ///  - scalar = WAITING_PO_CORRECTION and some live group is WAITING_PO_CORRECTION → not blocked (group-scoped return;
    ///    the returned group is refused by its own status, siblings keep their actions)
    ///  - scalar = WAITING_PO_CORRECTION and no live group is WAITING_PO_CORRECTION → BLOCKED (single group, several
    ///    groups, or none; complete or incomplete history; with or without payment rows)
    /// </summary>
    public static LegacyFinanceReturnBlock EvaluateFinancialBlock(LegacyFinanceReturnFacts f)
    {
        if (!string.Equals(f.ScalarStatusCode, RequestConstants.Statuses.WaitingPoCorrection, StringComparison.Ordinal))
            return new LegacyFinanceReturnBlock(false, "estado do pedido não é WAITING_PO_CORRECTION");
        if (f.LiveGroups.Any(g => string.Equals(g.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection, StringComparison.Ordinal)))
            return new LegacyFinanceReturnBlock(false, "devolução por grupo: o grupo devolvido está em WAITING_PO_CORRECTION e é recusado pelas suas próprias regras; os restantes grupos mantêm as suas ações");
        var detail = f.LiveGroups.Count switch
        {
            0 => "sem grupo P.O. ativo que carregue a devolução",
            1 => $"o único grupo ativo está em {f.LiveGroups[0].Status}; a devolução ao nível do pedido não foi corrigida" + (f.LatestFinanceReturnUtc == null ? " (histórico de devolução ausente — evidência em falta não autoriza ações financeiras)" : ""),
            _ => $"{f.LiveGroups.Count} grupos ativos e nenhum em WAITING_PO_CORRECTION; o grupo devolvido é desconhecido (nunca se adivinha)"
        };
        return new LegacyFinanceReturnBlock(true, $"{ReasonCode}: {detail}");
    }
}
