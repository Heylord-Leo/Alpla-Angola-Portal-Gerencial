import { describe, it, expect } from 'vitest';
// Node-env vitest — source guards for the v2.245.0 print document, button wiring and print service (§28).
import doc from './RequestPrintDocument.tsx?raw';
import model from './requestPrintModel.ts?raw';
import svc from '../../../../lib/printService.ts?raw';
import edit from '../../RequestEdit.tsx?raw';
import hook from '../../hooks/useRequestDetail.ts?raw';

describe('printService', () => {
  it('is a thin window.print() wrapper — no popup/document.write/deps', () => {
    expect(svc).toMatch(/window\.print\(\)/);
    expect(svc).not.toMatch(/window\.open\(/);
    expect(svc).not.toMatch(/document\.write\(/);
    expect(svc).not.toMatch(/^import /m);
  });
  it('F/H: sets document.title from options and restores it via afterprint', () => {
    expect(svc).toMatch(/const originalTitle = document\.title/);
    expect(svc).toMatch(/document\.title = title/);
    expect(svc).toMatch(/addEventListener\('afterprint', restore\)/);
    expect(svc).toMatch(/document\.title = originalTitle/);
    expect(svc).toMatch(/removeEventListener\('afterprint', restore\)/);
  });
  it('G: restores the original title if window.print() throws', () => {
    expect(svc).toMatch(/catch \(e\)/);
    // restore() is called on the throw path before rethrowing.
    expect(svc).toMatch(/restore\(\);\s*\n\s*throw e/);
  });
});

describe('RequestPrintDocument', () => {
  it('portals to document.body and hides itself on screen / shows only in print', () => {
    expect(doc).toMatch(/createPortal\(/);
    expect(doc).toMatch(/document\.body/);
    expect(doc).toMatch(/request-print-root/);
    expect(doc).toMatch(/\.\$\{ROOT_CLASS\} \{ display: none; \}/);
    expect(doc).toMatch(/@media print/);
    expect(doc).toMatch(/body > \*:not\(\.\$\{ROOT_CLASS\}\) \{ display: none !important; \}/);
  });
  it('print CSS removes max-height / overflow / fixed positioning / transform', () => {
    expect(doc).toMatch(/max-height: none !important/);
    expect(doc).toMatch(/overflow: visible !important/);
    expect(doc).toMatch(/position: static !important/);
    expect(doc).toMatch(/transform: none !important/);
  });
  it('builds content from the model (no drawer DOM cloning), incl. all groups & full history', () => {
    expect(doc).toMatch(/buildRequestPrintModel\(detail, projection, printedByName\)/);
    expect(doc).toMatch(/m\.groups\.map\(/);
    expect(doc).toMatch(/m\.history\.map\(/);
    expect(doc).not.toMatch(/innerHTML/);
    expect(doc).not.toMatch(/document\.write/);
    expect(doc).not.toMatch(/window\.open/);
  });
  it('B: shows "Gerado por <user>" from the authenticated printedBy name', () => {
    expect(doc).toMatch(/printedByName/);
    expect(doc).toMatch(/Gerado por \$\{m\.header\.printedBy\}/);
    expect(doc).toMatch(/buildRequestPrintModel\(detail, projection, printedByName\)/);
  });
  it('never calls an API or mutation from the print subtree', () => {
    expect(doc).not.toMatch(/\bapi\./);
    expect(doc).not.toMatch(/apiFetch/);
    expect(doc).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
  });
});

describe('RequestPrintDocument — Phase 2 polish (§18)', () => {
  it('history shows the human label as primary and the raw code as secondary', () => {
    expect(doc).toMatch(/\{h\.actionLabel\}/);
    expect(doc).toMatch(/\{h\.actionCode\}/);
  });
  it('H/I: history rows avoid split, section flows, headings avoid orphaning (no whole-history break-inside:avoid)', () => {
    expect(doc).toMatch(/\.rp-section \{ break-inside: auto; \}/);
    expect(doc).toMatch(/\.rp-heading \{ break-after: avoid; \}/);
    expect(doc).toMatch(/\.rp-hist-row \{ break-inside: avoid; \}/);
    expect(doc).toMatch(/\.rp-card \{ break-inside: avoid; \}/);
  });
  it('K: no application hacks against browser-generated header/footer (page counters)', () => {
    expect(doc).not.toMatch(/counter\(page\)/);
    expect(doc).not.toMatch(/@top-|@bottom-/);
    expect(doc).not.toMatch(/content:\s*["'].*(Página|Page).*["']/);
  });
  it('uses the ALPLA blue accent for headings (ink-light, no large solid fills)', () => {
    expect(doc).toMatch(/ALPLA_BLUE = '#004d90'/);
    expect(doc).not.toMatch(/print-color-adjust/);
  });
});

describe('requestPrintModel', () => {
  it('does not slice/truncate groups, line items, attachments or history', () => {
    expect(model).not.toMatch(/poGroups[^\n]*\.slice\(/);
    expect(model).not.toMatch(/statusHistory[^\n]*\.slice\(/);
    expect(model).not.toMatch(/lineItems[^\n]*\.slice\(/);
    // History sorts a COPY, never in place.
    expect(model).toMatch(/\[\.\.\.rows\]\s*\.sort/);
  });
  it('reuses shared formatters (no re-implemented currency/date math)', () => {
    expect(model).toMatch(/formatCurrencyAO/);
    expect(model).toMatch(/formatDate/);
    expect(model).toMatch(/formatDateTime/);
  });
});

describe('RequestEdit wiring', () => {
  it('renders an accessible Imprimir button with a printer icon, gated on data + preparing state', () => {
    expect(edit).toMatch(/<Printer size=\{14\}/);
    expect(edit).toMatch(/IMPRIMIR/);
    expect(edit).toMatch(/aria-label="Imprimir o pedido"/);
    expect(edit).toMatch(/disabled=\{isPreparingPrint\}/);
    expect(edit).toMatch(/\{detail && \(/); // only when request data is ready
  });
  it('invokes printService after a DOM commit (rAF), not window.print directly or via setTimeout', () => {
    expect(edit).toMatch(/printService\.print\(/);
    expect(edit).toMatch(/requestAnimationFrame\(\(\) => requestAnimationFrame/);
    expect(edit).not.toMatch(/setTimeout\([^,]*print/);
    expect(edit).not.toMatch(/window\.print\(\)/); // goes through the service
  });
  it('renders the print document fed by the loaded detail + projection + current user name', () => {
    expect(edit).toMatch(/<RequestPrintDocument detail=\{detail\} projection=\{workflowProjection\} printedByName=\{user\?\.fullName \?\? null\} \/>/);
  });
  it('B/C: builds a dynamic document title from the request number and passes it to printService', () => {
    expect(edit).toMatch(/toPrintFileTitle\(detail\.requestNumber\)/);
    expect(edit).toMatch(/printService\.print\(\{ documentTitle \}\)/);
  });
  it('K/L: print metadata is not persisted (no mutation/write from the print path)', () => {
    // handlePrint only reads user + detail and calls printService — no api.* mutation.
    expect(edit).not.toMatch(/api\.\w+\.(save|update|create|print)/i);
  });
  it('does not add a new API endpoint for printing', () => {
    // The print path reuses the already-loaded detail; no new api.requests.* print call.
    expect(edit).not.toMatch(/api\.requests\.(print|getPrint)/);
  });
});

describe('useRequestDetail exposes the raw detail for print (no refetch)', () => {
  it('keeps and returns the fetched RequestDetailsDto', () => {
    expect(hook).toMatch(/const \[detail, setDetail\] = useState<RequestDetailsDto \| null>/);
    expect(hook).toMatch(/setDetail\(data as RequestDetailsDto\)/);
    expect(hook).toMatch(/setDetail\(null\)/);
  });
});
