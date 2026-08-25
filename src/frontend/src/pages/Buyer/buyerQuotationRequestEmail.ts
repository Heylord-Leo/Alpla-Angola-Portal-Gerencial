// ─────────────────────────────────────────────────────────────────────────────
// "Solicitar cotação" — Outlook draft generator (Phase 3C). Pure and framework-free.
// This is NOT the Quotation Wizard: it only prepares an Outlook draft from a single canonical
// message (subject + full body). It NEVER includes prices, internal comparison, approval info,
// selected suppliers, or closed/approved items — only open PENDING_QUOTATION items.
//
// Delivery (no copy/paste): short drafts open via `mailto:` (compose + the user's signature);
// long drafts that exceed the mailto ceiling are delivered as an `.eml` DRAFT (X-Unsent: 1) which
// Windows opens in Outlook as an editable compose window carrying the COMPLETE body. Both paths use
// the same canonical body, so preview and Outlook can never drift.
// ─────────────────────────────────────────────────────────────────────────────
import type { BuyerWorkspace, BuyerWorkspaceItem } from '../../types/buyerWorkspace';

// Practical cross-platform mailto ceiling (Chromium ~2000, Windows ShellExecute ~2048). Above it the
// body risks truncation, so we switch to the .eml draft (which has no such limit).
export const MAILTO_MAX_LENGTH = 2000;

/** Only items still OPEN for a new quotation (canonical PENDING_QUOTATION bucket). */
export function openItemsForQuotation(items: BuyerWorkspaceItem[]): BuyerWorkspaceItem[] {
  return items.filter(i => i.coverageBucket === 'PENDING_QUOTATION');
}

export function buildSubject(requestNumber: string): string {
  return `Solicitação de Cotação - ${requestNumber}`;
}

function itemLine(i: BuyerWorkspaceItem, idx: number): string {
  const code = i.itemCatalogCode ? `[${i.itemCatalogCode}] ` : '';
  const qty = formatQty(i.quantity);
  const unit = i.unitName ? ` ${i.unitName}` : '';
  return `${idx + 1}. ${code}${i.description} — ${qty}${unit}`;
}

function formatQty(q: number): string {
  return Number.isInteger(q) ? String(q) : new Intl.NumberFormat('pt-PT', { maximumFractionDigits: 3 }).format(q);
}

function fmtDate(iso?: string | null): string {
  if (!iso) return 'a definir';
  try { return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' }); } catch { return 'a definir'; }
}

export type EmailWorkspace = Pick<BuyerWorkspace, 'requestNumber' | 'title' | 'companyName' | 'companyTaxId' | 'plantName' | 'departmentName' | 'needByDateUtc'>;

/** The single canonical PT draft with the open-item list. Used for preview AND every delivery path. */
export function buildBody(ws: EmailWorkspace, openItems: BuyerWorkspaceItem[]): string {
  const titlePart = ws.title ? ` — ${ws.title}` : '';
  const lines = openItems.map(itemLine).join('\n');
  return [
    'Exmos. Senhores,',
    '',
    `No âmbito do pedido de compra ${ws.requestNumber}${titlePart}, solicitamos a V. Exas. o envio de cotação para os artigos abaixo indicados.`,
    '',
    'Dados do pedido:',
    `- Empresa: ${ws.companyName ?? '—'}`,
    `- NIF: ${ws.companyTaxId ?? '—'}`,
    `- Planta: ${ws.plantName ?? '—'}`,
    `- Departamento: ${ws.departmentName ?? '—'}`,
    `- Necessário até: ${fmtDate(ws.needByDateUtc)}`,
    '',
    'Artigos a cotar:',
    lines || '(sem itens em aberto)',
    '',
    'Agradecemos a indicação de preços, prazos de entrega e condições de pagamento.',
    '',
    'Com os melhores cumprimentos,',
  ].join('\n');
}

export function buildMailto(subject: string, body: string): string {
  return `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

// Base64-encode a UTF-8 string via btoa (available in browsers and Node 16+). We first map the UTF-8
// bytes to a Latin1 string so btoa handles accents correctly. RFC2047-encode a header so ç/ã survive.
function base64Utf8(s: string): string {
  const bytes = new TextEncoder().encode(s);
  let bin = '';
  bytes.forEach(b => { bin += String.fromCharCode(b); });
  return btoa(bin);
}
function encodeHeader(value: string): string {
  return /^[\x20-\x7E]*$/.test(value) ? value : `=?UTF-8?B?${base64Utf8(value)}?=`;
}

/**
 * Build an RFC-822 `.eml` DRAFT. The `X-Unsent: 1` header makes Windows/Outlook open it as an
 * EDITABLE COMPOSE window (not a received message), carrying the complete UTF-8 body — the standard
 * no-copy-paste way to prefill a long Outlook draft. Line endings are CRLF per RFC 822.
 */
export function buildEml(subject: string, body: string): string {
  return [
    'X-Unsent: 1',
    'To: ',
    `Subject: ${encodeHeader(subject)}`,
    'MIME-Version: 1.0',
    'Content-Type: text/plain; charset=utf-8',
    'Content-Transfer-Encoding: 8bit',
    '',
    body,
  ].join('\r\n');
}

export function emlFilename(requestNumber: string): string {
  const safe = requestNumber.replace(/[^\w.-]+/g, '-');
  return `Cotacao_${safe}.eml`;
}

export interface QuotationDraft {
  subject: string;
  body: string;          // the ONE canonical full body
  fits: boolean;         // true = mailto path (compose + signature); false = .eml draft path
  mailtoFull: string;    // opens Outlook compose with the complete body (used when `fits`)
  eml: string;           // RFC-822 draft with the complete body (used when !fits)
  emlFilename: string;
  itemCount: number;
}

export function buildQuotationDraft(ws: EmailWorkspace, items: BuyerWorkspaceItem[]): QuotationDraft {
  const openItems = openItemsForQuotation(items);
  const subject = buildSubject(ws.requestNumber);
  const body = buildBody(ws, openItems);
  const mailtoFull = buildMailto(subject, body);
  return {
    subject,
    body,
    fits: mailtoFull.length <= MAILTO_MAX_LENGTH,
    mailtoFull,
    eml: buildEml(subject, body),
    emlFilename: emlFilename(ws.requestNumber),
    itemCount: openItems.length,
  };
}
