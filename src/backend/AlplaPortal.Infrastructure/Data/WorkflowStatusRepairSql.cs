namespace AlplaPortal.Infrastructure.Data;

/// <summary>
/// v2.229.1 — the exact repair statements of the RepairWorkflowStatusNamesAndAwaitingPo data
/// migration, shared with the integration tests so the migration and its pins can never drift.
///
/// <para><b>Why NCHAR() instead of literals.</b> The corruption being repaired was CAUSED by the
/// migration transport itself (BOM-less UTF-8 SQL read as ANSI by sqlcmd), so this repair must
/// not depend on the newly fixed transport: every non-ASCII character is constructed SQL-side
/// with NCHAR(codepoint), making the statements byte-safe through ANY encoding path.
/// á = NCHAR(225) · ç = NCHAR(231) · ã = NCHAR(227).</para>
///
/// <para>Every statement is idempotent: the WHERE clauses match only the wrong state, so re-runs
/// (and environments that never corrupted, like a fresh dev database) are exact no-ops.</para>
/// </summary>
public static class WorkflowStatusRepairSql
{
    /// <summary>ADVANCE_PAYMENT_REQUIRED → "Adiantamento Necessário".</summary>
    public const string RepairAdvancePaymentRequired = @"
UPDATE RequestStatuses
SET Name = N'Adiantamento Necess' + NCHAR(225) + N'rio'
WHERE Code = 'ADVANCE_PAYMENT_REQUIRED'
  AND Name <> N'Adiantamento Necess' + NCHAR(225) + N'rio';";

    /// <summary>WAITING_SUPPLIER_DELIVERY → "Ag. Entrega/Serviço".</summary>
    public const string RepairWaitingSupplierDelivery = @"
UPDATE RequestStatuses
SET Name = N'Ag. Entrega/Servi' + NCHAR(231) + N'o'
WHERE Code = 'WAITING_SUPPLIER_DELIVERY'
  AND Name <> N'Ag. Entrega/Servi' + NCHAR(231) + N'o';";

    /// <summary>WAITING_RECONCILIATION → "Ag. Reconciliação".</summary>
    public const string RepairWaitingReconciliation = @"
UPDATE RequestStatuses
SET Name = N'Ag. Reconcilia' + NCHAR(231) + NCHAR(227) + N'o'
WHERE Code = 'WAITING_RECONCILIATION'
  AND Name <> N'Ag. Reconcilia' + NCHAR(231) + NCHAR(227) + N'o';";

    /// <summary>
    /// The approved parked-request correction (REQ-17/08/2026-232 defect shape, exactly):
    /// a request still labeled QUOTATION_COMPLETED whose non-cancelled groups ALL sit in
    /// WAITING_PO (zero of N required P.O.s registered) moves to PO_REQUESTED.
    ///
    /// <para>What the predicate deliberately EXCLUDES: genuine zero-group QUOTATION_COMPLETED
    /// requests (the EXISTS demands a WAITING_PO group); partial-PO requests and anything at a
    /// later Finance/payment stage (the NOT EXISTS refuses any non-cancelled group in any other
    /// status, including WAITING_PO_CORRECTION — a corrected-PO shape aggregates on its own).
    /// Idempotent: a corrected request no longer matches the join.</para>
    /// </summary>
    public const string CorrectParkedAwaitingPoRequests = @"
UPDATE r
SET r.StatusId = (SELECT TOP 1 Id FROM RequestStatuses WHERE Code = 'PO_REQUESTED')
FROM Requests r
INNER JOIN RequestStatuses s ON s.Id = r.StatusId AND s.Code = 'QUOTATION_COMPLETED'
WHERE EXISTS (SELECT 1 FROM RequestPoGroups g
              WHERE g.RequestId = r.Id AND g.Status = 'WAITING_PO')
  AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g
                  WHERE g.RequestId = r.Id
                    AND g.Status NOT IN ('WAITING_PO', 'CANCELLED'));";

    /// <summary>Every repair statement, in application order.</summary>
    public static readonly string[] All =
    {
        RepairAdvancePaymentRequired,
        RepairWaitingSupplierDelivery,
        RepairWaitingReconciliation,
        CorrectParkedAwaitingPoRequests
    };
}
