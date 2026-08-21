using System;

namespace AlplaPortal.Domain.Services;

public enum PoSupplierBackfillAction
{
    /// <summary>Not eligible — nothing may be written.</summary>
    Skip = 0,
    /// <summary>Deterministically repairable: copy the request's supplier into the group.</summary>
    CopyHeaderSupplier = 1,
    /// <summary>No structured supplier source exists — requires assisted human confirmation.</summary>
    RequiresManualConfirmation = 2
}

public sealed record PoSupplierBackfillDecision(PoSupplierBackfillAction Action, string Reason);

/// <summary>
/// Eligibility rule for the Population-A supplier backfill (Phase 2 of the PO-flow repair):
/// a PO group whose <c>SupplierId</c> is null may be repaired ONLY from the deterministic
/// structured source — the owning request's own <c>SupplierId</c> — and only while doing so
/// cannot rewrite commercial history.
///
/// <para>The rule decides the ACTION alone. It never touches group status, approval state or
/// amounts — the executor copies SupplierId + name/NIF snapshots, writes an audit history row,
/// and nothing else. A request with no structured supplier anywhere (Population B, e.g.
/// REQ-31/07/2026-193) is never guessed: it requires explicit human confirmation.</para>
/// </summary>
public static class PoSupplierBackfillRule
{
    public static PoSupplierBackfillDecision Evaluate(
        int? groupSupplierId,
        string? groupStatus,
        int? requestSupplierId,
        bool supplierExists,
        bool supplierIsActive)
    {
        if (groupSupplierId != null)
            return new PoSupplierBackfillDecision(PoSupplierBackfillAction.Skip,
                "O grupo já possui fornecedor.");

        // Only pre-PO stages are repairable: once a P.O. was issued against the group, changing
        // the supplier identity retroactively would rewrite commercial history.
        var repairableStatuses = new[] { "PENDING", "WAITING_PO", "WAITING_PO_CORRECTION" };
        if (!Array.Exists(repairableStatuses,
                s => string.Equals(s, groupStatus, StringComparison.OrdinalIgnoreCase)))
            return new PoSupplierBackfillDecision(PoSupplierBackfillAction.Skip,
                $"Estado do grupo ({groupStatus}) não é reparável — a P.O. já avançou.");

        if (requestSupplierId == null)
            return new PoSupplierBackfillDecision(PoSupplierBackfillAction.RequiresManualConfirmation,
                "Pedido legado sem fornecedor estruturado — requer confirmação humana (nunca adivinhar).");

        if (!supplierExists)
            return new PoSupplierBackfillDecision(PoSupplierBackfillAction.Skip,
                "O fornecedor do cabeçalho do pedido não existe no cadastro.");

        if (!supplierIsActive)
            return new PoSupplierBackfillDecision(PoSupplierBackfillAction.RequiresManualConfirmation,
                "O fornecedor do cabeçalho está inativo — confirmar antes de reparar.");

        return new PoSupplierBackfillDecision(PoSupplierBackfillAction.CopyHeaderSupplier,
            "Determinístico: copiar o fornecedor do cabeçalho do pedido para o grupo.");
    }
}
