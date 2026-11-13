/**
 * Supplier account credit — Portuguese labels of the request-history / Finance-history event codes
 * (`RequestStatusHistory.ActionTaken`, backend `SupplierCreditConstants.HistoryActions` / `WorkflowEventCodes`).
 *
 * One source for every renderer (request page "Histórico do Pedido", Finanças → Histórico, Print View) so the
 * request history never shows the raw `SUPPLIER_CREDIT_*` code (DEV round 1 feedback, 2026-11-12). Presentation
 * only: the stored codes and data are untouched and remain available for audit.
 */
export const SUPPLIER_CREDIT_EVENT_LABELS: Readonly<Record<string, string>> = Object.freeze({
    SUPPLIER_CREDIT_RESERVED: 'Crédito reservado',
    SUPPLIER_CREDIT_RESERVATION_CHANGED: 'Reserva de crédito alterada',
    SUPPLIER_CREDIT_RELEASED: 'Reserva de crédito libertada',
    SUPPLIER_CREDIT_APPLIED: 'Crédito aplicado',
    SUPPLIER_CREDIT_REVERSED: 'Crédito revertido',
});

/** The Portuguese label of a supplier-credit event code, or null for any other code. */
export function resolveSupplierCreditEventLabel(code: string | null | undefined): string | null {
    if (!code) return null;
    return SUPPLIER_CREDIT_EVENT_LABELS[code] ?? null;
}
