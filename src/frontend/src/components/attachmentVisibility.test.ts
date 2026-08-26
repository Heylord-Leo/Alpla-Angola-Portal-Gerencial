import { describe, it, expect } from 'vitest';
import { selectUnmappedAttachments } from './attachmentVisibility';

// Phase 4B — an authorized attachment must never be counted-but-hidden. Anything without a dedicated card
// (e.g. PAYMENT_SOURCE_DOCUMENT, or an unknown legacy type) is surfaced by the fallback bucket.
const KNOWN = ['PROFORMA', 'QUOTATION', 'PO', 'PAYMENT_SCHEDULE', 'PAYMENT_PROOF', 'RECEIPT', 'SUPPORTING'];
const att = (id: string, type: string) => ({ id, attachmentTypeCode: type });

describe('selectUnmappedAttachments', () => {
  it('surfaces PAYMENT_SOURCE_DOCUMENT (no dedicated card) in the fallback', () => {
    const out = selectUnmappedAttachments([att('a', 'PAYMENT_SOURCE_DOCUMENT')], KNOWN, new Set());
    expect(out.map(a => a.id)).toEqual(['a']);
  });
  it('keeps known types OUT of the fallback (they render in their cards)', () => {
    const out = selectUnmappedAttachments([att('a', 'PROFORMA'), att('b', 'QUOTATION'), att('c', 'PO')], KNOWN, new Set());
    expect(out).toEqual([]);
  });
  it('surfaces an unknown/legacy type in the fallback', () => {
    const out = selectUnmappedAttachments([att('x', 'SOME_LEGACY_TYPE')], KNOWN, new Set());
    expect(out.map(a => a.id)).toEqual(['x']);
  });
  it('does NOT duplicate a file already shown in the source-document section', () => {
    const out = selectUnmappedAttachments([att('a', 'PAYMENT_SOURCE_DOCUMENT')], KNOWN, new Set(['a']));
    expect(out).toEqual([]);
  });
  it('mixes: known cards excluded, unmapped kept, source-doc dedup applied', () => {
    const list = [att('p', 'PROFORMA'), att('s1', 'PAYMENT_SOURCE_DOCUMENT'), att('s2', 'PAYMENT_SOURCE_DOCUMENT')];
    const out = selectUnmappedAttachments(list, KNOWN, new Set(['s1']));
    expect(out.map(a => a.id)).toEqual(['s2']); // p is known, s1 already shown, s2 surfaced
  });
});
