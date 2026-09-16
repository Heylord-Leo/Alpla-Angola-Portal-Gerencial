import { createPortal } from 'react-dom';
import type { RequestDetailsDto } from '../../../../types';
import { buildRequestPrintModel, type PrintField, type RequestPrintModel } from './requestPrintModel';

// v2.245.0 Request Print View — Phase 1. Renders a complete, data-driven printable document for a
// request, portaled to <body> so it escapes the fixed/transformed drawer. It is HIDDEN on screen and
// shown ONLY under @media print, where all other app/drawer chrome is hidden. Content comes entirely
// from the already-loaded RequestDetailsDto (+ optional workflow projection) via buildRequestPrintModel
// — complete history, every group, regardless of collapsed sections or scroll position.

const ROOT_CLASS = 'request-print-root';

interface Props {
  detail: RequestDetailsDto | null;
  projection?: any | null;
  /** Display name of the currently authenticated user initiating the print (never persisted). */
  printedByName?: string | null;
}

// A positive "only the print root prints" strategy: hide every direct child of <body> except the print
// root, then normalize the root to normal flow. Ink-light: white ground, dark text, hairline borders,
// no solid fills (independent of the browser's "print background graphics" setting).
const PRINT_STYLE = `
.${ROOT_CLASS} { display: none; }
@media print {
  body > *:not(.${ROOT_CLASS}) { display: none !important; }
  .${ROOT_CLASS} {
    display: block !important;
    position: static !important;
    inset: auto !important;
    width: auto !important;
    max-width: 100% !important;
    max-height: none !important;
    overflow: visible !important;
    transform: none !important;
    background: #fff !important;
    color: #111 !important;
    margin: 0 !important;
    padding: 0 !important;
    z-index: auto !important;
  }
  .${ROOT_CLASS} * { box-shadow: none !important; }
  html, body { background: #fff !important; }
  .rp-section { break-inside: auto; }
  .rp-heading { break-after: avoid; }
  .rp-card { break-inside: avoid; }
  .rp-hist-row { break-inside: avoid; }
  @page { margin: 14mm; }
}
`;

// ALPLA blue (light-theme primary). Used as a literal — the drawer may be in dark theme, but the print
// document must read consistently on white regardless of the on-screen theme.
const ALPLA_BLUE = '#004d90';

const page: React.CSSProperties = {
  maxWidth: '190mm', margin: '0 auto', padding: '0 3mm',
  fontFamily: 'Arial, Helvetica, sans-serif', color: '#111', fontSize: '9px', lineHeight: 1.4,
};
const h2: React.CSSProperties = { fontSize: '10px', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', margin: '13px 0 5px', color: ALPLA_BLUE, borderBottom: `1px solid ${ALPLA_BLUE}`, paddingBottom: 2 };
const cardStyle: React.CSSProperties = { border: '1px solid #ccc', borderRadius: 3, padding: '5px 8px', marginBottom: 5 };
const muted: React.CSSProperties = { color: '#666' };

function FieldGrid({ fields }: { fields: PrintField[] }) {
  if (fields.length === 0) return null;
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: '1px 16px' }}>
      {fields.map((f, i) => (
        <div key={i} style={{ display: 'flex', gap: 5, padding: '0.5px 0' }}>
          <span style={{ fontWeight: 700, color: '#444', whiteSpace: 'nowrap' }}>{f.label}:</span>
          <span style={{ color: '#111' }}>{f.value}</span>
        </div>
      ))}
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rp-section" style={{ marginBottom: 6 }}>
      <h2 className="rp-heading" style={h2}>{title}</h2>
      {children}
    </section>
  );
}

export function RequestPrintDocument({ detail, projection = null, printedByName = null }: Props) {
  if (!detail) return null;
  const m: RequestPrintModel = buildRequestPrintModel(detail, projection, printedByName);
  const generatedLine = m.header.printedBy
    ? `Gerado por ${m.header.printedBy} em ${m.header.generatedAt}`
    : `Gerado em ${m.header.generatedAt}`;

  return createPortal(
    <div className={ROOT_CLASS} aria-hidden="true">
      <style>{PRINT_STYLE}</style>
      <div style={page}>
        {/* Header — compact: brand line, then number + status on one row, title, then muted meta. */}
        <header style={{ borderBottom: `2px solid ${ALPLA_BLUE}`, paddingBottom: 5, marginBottom: 2 }}>
          <div style={{ fontSize: '8px', fontWeight: 800, letterSpacing: '0.14em', color: ALPLA_BLUE, textTransform: 'uppercase' }}>Portal Gerencial</div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12, marginTop: 1, flexWrap: 'wrap' }}>
            <div style={{ fontSize: '15px', fontWeight: 800, color: '#111' }}>{m.header.requestNumber}</div>
            <div style={{ fontSize: '9px', fontWeight: 700, border: `1px solid ${ALPLA_BLUE}`, color: ALPLA_BLUE, borderRadius: 999, padding: '1px 9px' }}>{m.header.statusName}</div>
          </div>
          <div style={{ fontSize: '11px', fontWeight: 600, marginTop: 1, color: '#111' }}>{m.header.title}</div>
          <div style={{ fontSize: '8px', ...muted, marginTop: 2 }}>
            {m.header.companyPlant ? `${m.header.companyPlant} · ` : ''}Detalhes do Pedido
          </div>
          <div style={{ fontSize: '8px', ...muted, marginTop: 1 }}>{generatedLine}</div>
        </header>

        {/* Workflow summary */}
        {m.workflow && (
          <Section title="Situação Operacional">
            <FieldGrid fields={m.workflow} />
          </Section>
        )}

        {/* General data */}
        <Section title="Dados Gerais">
          <FieldGrid fields={m.general} />
          {m.description && (
            <div style={{ marginTop: 8 }}>
              <div style={{ fontWeight: 700, color: '#333' }}>Descrição / Justificação:</div>
              <div style={{ whiteSpace: 'pre-wrap', color: '#111', marginTop: 2 }}>{m.description}</div>
            </div>
          )}
        </Section>

        {/* Line items */}
        {m.lineItems.length > 0 && (
          <Section title={`Itens (${m.lineItems.length})`}>
            {m.lineItems.map((li, i) => (
              <div key={i} className="rp-card" style={{ ...cardStyle, marginBottom: 4 }}>
                <FieldGrid fields={li.fields} />
              </div>
            ))}
          </Section>
        )}

        {/* Groups / lotes */}
        {m.groups.length > 0 && (
          <Section title={`Grupos / Lotes (${m.groups.length})`}>
            {m.groups.map((g, i) => (
              <div key={i} className="rp-card" style={cardStyle}>
                <div style={{ fontWeight: 800, marginBottom: 4 }}>{g.title}</div>
                <FieldGrid fields={g.fields} />
                {g.payments.length > 0 && (
                  <div style={{ marginTop: 6 }}>
                    <div style={{ fontWeight: 700, color: '#333', marginBottom: 2 }}>Pagamentos:</div>
                    {g.payments.map((p, j) => (
                      <div key={j} style={{ borderTop: '1px dashed #bbb', paddingTop: 3, marginTop: 3 }}>
                        <FieldGrid fields={p} />
                      </div>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </Section>
        )}

        {/* Quotations */}
        {m.quotations.length > 0 && (
          <Section title={`Cotações (${m.quotations.length})`}>
            {m.quotations.map((q, i) => (
              <div key={i} className="rp-card" style={{ ...cardStyle, borderColor: q.selected ? '#111' : '#999' }}>
                <FieldGrid fields={q.fields} />
              </div>
            ))}
          </Section>
        )}

        {/* Approvals */}
        {m.approvals.length > 0 && (
          <Section title={`Aprovações (${m.approvals.length})`}>
            {m.approvals.map((a, i) => (
              <div key={i} className="rp-card" style={cardStyle}>
                <FieldGrid fields={a.fields} />
              </div>
            ))}
          </Section>
        )}

        {/* Attachments */}
        {m.attachments.length > 0 && (
          <Section title={`Documentos (${m.attachments.length})`}>
            {m.attachments.map((a, i) => (
              <div key={i} style={{ borderBottom: '1px solid #ddd', padding: '3px 0' }}>
                <FieldGrid fields={a.fields} />
              </div>
            ))}
          </Section>
        )}

        {/* History — complete, oldest → newest */}
        {m.history.length > 0 && (
          <Section title={`Histórico (${m.history.length})`}>
            {m.history.map((h, i) => (
              <div key={i} className="rp-hist-row" style={{ borderLeft: `2px solid ${ALPLA_BLUE}`, paddingLeft: 7, marginBottom: 4 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, flexWrap: 'wrap' }}>
                  <span style={{ fontWeight: 600, color: '#111' }}>{h.actionLabel}</span>
                  <span style={{ ...muted, whiteSpace: 'nowrap', fontSize: '8px' }}>{h.at}</span>
                </div>
                <div style={{ ...muted, fontSize: '8.5px' }}>{h.actor}</div>
                {h.comment && <div style={{ whiteSpace: 'pre-wrap', marginTop: 1, color: '#111' }}>{h.comment}</div>}
                <div style={{ ...muted, fontSize: '7.5px', marginTop: 1 }}>
                  {h.newStatus ? `Estado: ${h.newStatus}` : ''}{h.newStatus ? ' · ' : ''}{h.actionCode}
                </div>
              </div>
            ))}
          </Section>
        )}

        <footer style={{ marginTop: 14, paddingTop: 6, borderTop: '1px solid #ccc', fontSize: '8px', color: '#777', textAlign: 'center' }}>
          Documento gerado pelo Portal Gerencial
        </footer>
      </div>
    </div>,
    document.body,
  );
}

export default RequestPrintDocument;
