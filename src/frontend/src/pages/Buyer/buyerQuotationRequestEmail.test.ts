import { describe, it, expect } from 'vitest';
import {
  MAILTO_MAX_LENGTH, openItemsForQuotation, buildSubject, buildBody, buildMailto,
  buildEml, emlFilename, buildQuotationDraft, type EmailWorkspace,
} from './buyerQuotationRequestEmail';
import type { BuyerWorkspaceItem } from '../../types/buyerWorkspace';

const plastico: EmailWorkspace = {
  requestNumber: 'REQ-11/08/2026-234', title: 'Material de escritório',
  companyName: 'AlplaPLASTICO', companyTaxId: '5417567485', plantName: 'Viana 1', departmentName: 'TI', needByDateUtc: '2026-09-01T00:00:00Z',
};
const sopro: EmailWorkspace = { ...plastico, companyName: 'AlplaSOPRO', companyTaxId: '5001760246' };

let seq = 0;
function item(over: Partial<BuyerWorkspaceItem>): BuyerWorkspaceItem {
  return { id: `id-${seq++}`, lineNumber: 1, description: 'Item', quantity: 1, coverageBucket: 'PENDING_QUOTATION', canCloseNotQuoted: false, ...over };
}
function items(n: number): BuyerWorkspaceItem[] {
  return Array.from({ length: n }, (_, i) => item({ lineNumber: i + 1, itemCatalogCode: `ART-${1000 + i}`, description: `Artigo de manutenção representativo número ${i + 1}`, quantity: (i % 9) + 1, unitName: 'unidade' }));
}
function mailtoBody(m: string): string { return decodeURIComponent(m.split('&body=')[1] ?? ''); }

describe('open-item filtering', () => {
  it('includes only PENDING_QUOTATION items', () => {
    const list = [
      item({ description: 'Aberto', coverageBucket: 'PENDING_QUOTATION' }),
      item({ description: 'Cotado', coverageBucket: 'QUOTED_READY_FOR_BATCH' }),
      item({ description: 'Aprovado', coverageBucket: 'APPROVED' }),
    ];
    expect(openItemsForQuotation(list).map(i => i.description)).toEqual(['Aberto']);
  });
});

describe('canonical body — content + company NIF', () => {
  it('subject identifies the request', () => {
    expect(buildSubject('REQ-11/08/2026-234')).toBe('Solicitação de Cotação - REQ-11/08/2026-234');
  });

  it('includes the company NIF (AlplaPLASTICO → 5417567485, AlplaSOPRO → 5001760246)', () => {
    expect(buildBody(plastico, items(1))).toContain('- NIF: 5417567485');
    expect(buildBody(sopro, items(1))).toContain('- NIF: 5001760246');
  });

  it('includes request context and open items; never leaks prices/approval/selected suppliers', () => {
    const body = buildBody(plastico, [item({ itemCatalogCode: 'ART-100', description: 'Papel A4', quantity: 10, unitName: 'resma', selectedQuotationSummary: 'Fornecedor X · 999 AOA' })]);
    expect(body).toContain('Empresa: AlplaPLASTICO');
    expect(body).toContain('[ART-100] Papel A4 — 10 resma');
    expect(body).not.toContain('AOA');
    expect(body).not.toContain('999');
    expect(body).not.toContain('Fornecedor X');
    expect(body.toLowerCase()).not.toContain('aprovaç');
  });
});

describe('every item is present for 1 / 5 / 19 / 30 / 50 items', () => {
  for (const n of [1, 5, 19, 30, 50]) {
    it(`${n} items → canonical body lists item 1..${n}`, () => {
      const draft = buildQuotationDraft(plastico, items(n));
      expect(draft.itemCount).toBe(n);
      expect(draft.body).toContain('1. [ART-1000]');
      expect(draft.body).toContain(`${n}. [ART-${1000 + n - 1}]`);
      // The chosen delivery ALWAYS carries the complete body (mailto when it fits, else .eml).
      if (draft.fits) {
        expect(mailtoBody(draft.mailtoFull)).toBe(draft.body);
      } else {
        expect(draft.eml).toContain(`${n}. [ART-${1000 + n - 1}]`); // full body in the .eml draft
      }
    });
  }
});

describe('delivery strategy — no clipboard dependency in the accepted path', () => {
  it('small draft → mailto compose with the EXACT canonical body', () => {
    const draft = buildQuotationDraft(plastico, items(3));
    expect(draft.fits).toBe(true);
    expect(draft.mailtoFull.length).toBeLessThanOrEqual(MAILTO_MAX_LENGTH);
    expect(mailtoBody(draft.mailtoFull)).toBe(draft.body); // preview == Outlook, no drift
  });

  it('large draft (19 items) → .eml draft carries the COMPLETE body, no paste required', () => {
    const draft = buildQuotationDraft(plastico, items(19));
    expect(draft.fits).toBe(false); // 19 items exceeds the mailto ceiling
    // The .eml opens Outlook as an editable compose (X-Unsent) with the full body — no copy/paste.
    expect(draft.eml).toContain('X-Unsent: 1');
    expect(draft.eml).toContain('Content-Type: text/plain; charset=utf-8');
    expect(draft.eml).toContain('19. [ART-1018]');
    expect(draft.eml).toContain('- NIF: 5417567485');
  });
});

describe('.eml helpers', () => {
  it('buildEml emits an X-Unsent editable draft with an RFC2047-encoded accented subject', () => {
    const eml = buildEml('Solicitação de Cotação - REQ-1', 'corpo');
    expect(eml.startsWith('X-Unsent: 1')).toBe(true);
    expect(eml).toContain('Subject: =?UTF-8?B?'); // accented subject encoded
    expect(eml).toContain('\r\n\r\ncorpo'); // header/body separator + CRLF
  });
  it('emlFilename is filesystem-safe', () => {
    expect(emlFilename('REQ-11/08/2026-234')).toBe('Cotacao_REQ-11-08-2026-234.eml');
  });
  it('buildMailto encodes subject and body', () => {
    expect(buildMailto('a & b', 'l1\nl2')).toContain('%0A');
  });
});
