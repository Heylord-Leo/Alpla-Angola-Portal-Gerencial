import { describe, it, expect } from 'vitest';
import { canonicalStatusLabel, CANONICAL_STATUS_LABELS } from './statusLabels';

// v2.245.2 — canonical, display-only status labels (no DB/migration change).
describe('canonicalStatusLabel', () => {
  it('overrides the ambiguous persisted WAITING_RECEIPT name with the supplier-receipt wording', () => {
    expect(canonicalStatusLabel('WAITING_RECEIPT', 'Aguardando Recibo')).toBe('Aguardando Recibo do Fornecedor');
    expect(CANONICAL_STATUS_LABELS.WAITING_RECEIPT).toBe('Aguardando Recibo do Fornecedor');
  });

  it('falls back to the persisted name when there is no override', () => {
    expect(canonicalStatusLabel('PAYMENT_COMPLETED', 'Pagamento Realizado')).toBe('Pagamento Realizado');
  });

  it('never returns blank', () => {
    expect(canonicalStatusLabel(undefined, undefined)).toBe('');
    expect(canonicalStatusLabel('UNKNOWN', null)).toBe('');
  });
});
