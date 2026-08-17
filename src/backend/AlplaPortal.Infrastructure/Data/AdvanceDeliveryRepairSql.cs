namespace AlplaPortal.Infrastructure.Data;

/// <summary>
/// v2.229.3 — the exact repair statements of the HandoffParkedAdvancePaidGroupsToDelivery data
/// migration, shared with the integration tests so the migration and its pins can never drift.
///
/// <para>Repairs the REQ-17/08/2026-232 defect shape: groups whose advance was genuinely
/// confirmed (COMPLETED ADVANCE row) but which stayed parked in ADVANCE_PAYMENT_COMPLETED
/// because the ADVANCE_PAYMENT_COMPLETED → WAITING_SUPPLIER_DELIVERY transition never existed.
/// All statements are idempotent (corrected rows stop matching) and pure ASCII.</para>
/// </summary>
public static class AdvanceDeliveryRepairSql
{
    /// <summary>
    /// Group repair — narrow by construction:
    /// only ADVANCE_PAYMENT_COMPLETED groups (later stages — WAITING_RECEIPT, IN_FOLLOWUP,
    /// WAITING_RECONCILIATION, WAITING_FISCAL_RECEIPT, COMPLETED, CANCELLED — never match the
    /// Status filter); only with a COMPLETED ADVANCE row (the payment must be a fact); only
    /// while the operational receipt is unstamped; only on live requests; and only while no
    /// reconciliation of the request exists (a reconciliation row proves the flow progressed
    /// past delivery through some other path).
    /// </summary>
    public const string HandoffParkedGroups = @"
UPDATE g
SET g.Status = 'WAITING_SUPPLIER_DELIVERY'
FROM RequestPoGroups g
INNER JOIN Requests r ON r.Id = g.RequestId
INNER JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Status = 'ADVANCE_PAYMENT_COMPLETED'
  AND g.OperationalReceiptCompletedAtUtc IS NULL
  AND r.IsCancelled = 0
  AND rs.Code NOT IN ('REJECTED', 'CANCELLED', 'COMPLETED')
  AND EXISTS (SELECT 1 FROM RequestPayments p
              WHERE p.RequestPoGroupId = g.Id
                AND p.PaymentType = 'ADVANCE'
                AND p.PaymentStatus = 'COMPLETED')
  AND NOT EXISTS (SELECT 1 FROM RequestReconciliations rec
                  WHERE rec.RequestId = g.RequestId);";

    /// <summary>
    /// Parent repair — required because the Receiving workspace is request-status-driven, so a
    /// stale ADVANCE_PAYMENT_COMPLETED parent would keep the corrected group undiscoverable.
    /// SQL equivalent of the calculator's furthest-behind rule for the defect projection: the
    /// parent moves ONLY when it is itself parked in ADVANCE_PAYMENT_COMPLETED, at least one
    /// non-cancelled group now sits in WAITING_SUPPLIER_DELIVERY, and NO non-cancelled group
    /// sits in any earlier/other state (a sibling still paying or parked keeps the parent
    /// untouched, to self-heal on the next aggregation touch).
    /// </summary>
    public const string HandoffParkedParents = @"
UPDATE r
SET r.StatusId = (SELECT TOP 1 Id FROM RequestStatuses WHERE Code = 'WAITING_SUPPLIER_DELIVERY')
FROM Requests r
INNER JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE rs.Code = 'ADVANCE_PAYMENT_COMPLETED'
  AND r.IsCancelled = 0
  AND EXISTS (SELECT 1 FROM RequestPoGroups g
              WHERE g.RequestId = r.Id AND g.Status = 'WAITING_SUPPLIER_DELIVERY')
  AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g
                  WHERE g.RequestId = r.Id
                    AND g.Status NOT IN ('WAITING_SUPPLIER_DELIVERY', 'CANCELLED', 'COMPLETED',
                                         'WAITING_RECEIPT', 'IN_FOLLOWUP', 'WAITING_RECONCILIATION',
                                         'WAITING_FISCAL_RECEIPT'));";

    /// <summary>Both statements, in application order (groups first, then parents).</summary>
    public static readonly string[] All = { HandoffParkedGroups, HandoffParkedParents };
}
