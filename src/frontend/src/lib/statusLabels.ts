// v2.245.2 — canonical, display-only status label overrides.
//
// The DB seed name for WAITING_RECEIPT is the ambiguous "Aguardando Recibo" (it does not say WHOSE
// receipt). Per the approved domain glossary we surface "Aguardando Recibo do Fornecedor" at the display
// layer WITHOUT changing the persisted DB row or any migration (rule R18 — internal codes/rows are never
// re-semanticized). Apply this to any header/badge that would otherwise render the raw persisted name.

export const CANONICAL_STATUS_LABELS: Record<string, string> = {
  WAITING_RECEIPT: 'Aguardando Recibo do Fornecedor',
};

/**
 * Canonical display label for a status code, falling back to the provided (persisted) name when there is
 * no override. Never returns blank.
 */
export function canonicalStatusLabel(
  code: string | null | undefined,
  fallback: string | null | undefined,
): string {
  if (code && CANONICAL_STATUS_LABELS[code]) return CANONICAL_STATUS_LABELS[code];
  return fallback ?? '';
}
