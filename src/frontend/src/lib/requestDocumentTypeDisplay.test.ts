import { describe, it, expect } from 'vitest';
import {
  resolveRequestLevelDocumentTypeDisplay, REQUEST_LEVEL_DECLARED_LABEL, REQUEST_LEVEL_DECLARED_HINT,
} from './requestDocumentTypeDisplay';

// v2.245.9 — the request-level "Tipo de documento anexado" is creation-time metadata; once operational
// groups exist, the group classification is authoritative and the header must never read as a pending
// "Não classificado" for a different concept.

describe('resolveRequestLevelDocumentTypeDisplay', () => {
  it('DRAFT keeps the editable request-level field (creation/edit flow unchanged, may still be required)', () => {
    expect(resolveRequestLevelDocumentTypeDisplay({ status: 'DRAFT', hasOperationalGroups: false, value: null })).toEqual({ mode: 'editable' });
    expect(resolveRequestLevelDocumentTypeDisplay({ status: 'DRAFT', hasOperationalGroups: false, value: 'PROFORMA' })).toEqual({ mode: 'editable' });
  });

  it('read-only BEFORE operational groups exist keeps the declaration (it still seeds Final Approval)', () => {
    for (const status of ['WAITING_AREA_APPROVAL', 'WAITING_FINAL_APPROVAL', 'ADJUSTMENT_REQUESTED']) {
      expect(resolveRequestLevelDocumentTypeDisplay({ status, hasOperationalGroups: false, value: null })).toEqual({ mode: 'declaration' });
      expect(resolveRequestLevelDocumentTypeDisplay({ status, hasOperationalGroups: false, value: 'INVOICE' })).toEqual({ mode: 'declaration' });
    }
  });

  it('with operational groups and a declared value → relabelled as the creation-time declaration, distinct from the group classification', () => {
    const display = resolveRequestLevelDocumentTypeDisplay({ status: 'COMPLETED', hasOperationalGroups: true, value: 'PROFORMA' });
    expect(display).toEqual({ mode: 'declared', label: REQUEST_LEVEL_DECLARED_LABEL, hint: REQUEST_LEVEL_DECLARED_HINT });
    expect(REQUEST_LEVEL_DECLARED_LABEL).not.toMatch(/anexado/);
    expect(REQUEST_LEVEL_DECLARED_HINT).toMatch(/classificação operacional/i);
    expect(REQUEST_LEVEL_DECLARED_HINT).toMatch(/cada grupo/);
  });

  it('with operational groups and NO declared value (legacy request) → suppressed: never a misleading "Não classificado"', () => {
    for (const status of ['WAITING_RECEIPT', 'IN_FOLLOWUP', 'WAITING_FISCAL_RECEIPT', 'COMPLETED', 'PAYMENT_COMPLETED']) {
      expect(resolveRequestLevelDocumentTypeDisplay({ status, hasOperationalGroups: true, value: null })).toEqual({ mode: 'hidden' });
      expect(resolveRequestLevelDocumentTypeDisplay({ status, hasOperationalGroups: true, value: '' })).toEqual({ mode: 'hidden' });
      expect(resolveRequestLevelDocumentTypeDisplay({ status, hasOperationalGroups: true, value: 'UNCLASSIFIED' })).toEqual({ mode: 'hidden' });
    }
  });

  it('completed request rendering is stable: same input → same display', () => {
    const a = resolveRequestLevelDocumentTypeDisplay({ status: 'COMPLETED', hasOperationalGroups: true, value: 'INVOICE' });
    const b = resolveRequestLevelDocumentTypeDisplay({ status: 'COMPLETED', hasOperationalGroups: true, value: 'INVOICE' });
    expect(a).toEqual(b);
  });
});
