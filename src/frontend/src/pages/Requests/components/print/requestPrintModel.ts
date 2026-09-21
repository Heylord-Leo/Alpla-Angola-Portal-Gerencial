import type {
  RequestDetailsDto, RequestPoGroupDto, RequestStatusHistoryDto,
  RequestAttachmentDto, SavedQuotationDto, RequestLineItemDto, ApprovalBatchSummary,
} from '../../../../types';
import { formatCurrencyAO, formatDate, formatDateTime } from '../../../../lib/utils';

// v2.245.0 Request Print View — Phase 1. PURE mapping from the already-loaded RequestDetailsDto
// (+ optional workflow projection) into a flat, formatted, print-ready view-model. No React, no
// business-rule re-interpretation: it reuses existing DTO fields and shared formatters, omits empty
// sections, and never mutates its inputs. The component renders exactly what this returns.

export interface PrintField { label: string; value: string; }

export interface PrintGroup {
  title: string;
  fields: PrintField[];
  payments: PrintField[][]; // one row (list of fields) per payment
}

export interface PrintLineItem { fields: PrintField[]; }

export interface PrintQuotation { fields: PrintField[]; selected: boolean; }

export interface PrintApproval { fields: PrintField[]; }

export interface PrintAttachment { fields: PrintField[]; voided: boolean; }

export interface PrintHistoryEntry {
  at: string;
  /** Human-readable PT label for the action (falls back to a normalized code). */
  actionLabel: string;
  /** Raw technical code, kept for auditability as secondary detail. */
  actionCode: string;
  actor: string;
  newStatus: string;
  comment: string | null;
}

// Frontend-only PRESENTATION mapping — never changes stored data. Only deterministic, known action
// codes are mapped; anything unknown falls back to a safe normalized label (§3, §4).
export const HISTORY_ACTION_LABELS: Record<string, string> = {
  CREATED: 'Pedido criado',
  SUBMIT: 'Pedido submetido',
  ITEM_ADDED: 'Item adicionado',
  ITEM_UPDATED: 'Item atualizado',
  ITEM_REMOVED: 'Item removido',
  'DOCUMENTO ADICIONADO': 'Documento adicionado',
  DOCUMENT_ADDED: 'Documento adicionado',
  'DOCUMENTO REMOVIDO': 'Documento removido',
  DOCUMENTO_ORIGEM_ADICIONADO: 'Documento de origem adicionado',
  DOCUMENTO_ORIGEM_ANULADO: 'Documento de origem anulado',
  DOCUMENTO_ORIGEM_SUBSTITUIDO: 'Documento de origem substituído',
  APPROVE: 'Aprovação realizada',
  REJECT: 'Rejeição',
  REQUEST_ADJUSTMENT: 'Devolvido para reajuste',
  FINANCE_RETURN_ADJUSTMENT: 'Devolvido por Finanças para correção',
  REGISTER_PO: 'P.O. registrada',
  REREGISTER_PO: 'P.O. corrigida e registrada',
  BATCH_CREATED: 'Lote de aprovação criado',
  BATCH_CANDIDATES_SUBMITTED: 'Candidatos submetidos',
  BATCH_AREA_APPROVED: 'Aprovação de área',
  BATCH_FINAL_APPROVED: 'Aprovação final',
  BATCH_AREA_REJECTED: 'Rejeição de área',
  BATCH_FINAL_REJECTED: 'Rejeição final',
  BATCH_AREA_ADJUSTMENT: 'Devolvido (área)',
  BATCH_FINAL_ADJUSTMENT: 'Devolvido (final)',
  BATCH_RESUBMITTED: 'Reenviado',
  QUOTATION_ITEM_AWARDED: 'Item adjudicado',
  COTACAO_ADICIONADA: 'Cotação adicionada',
  COTACAO_SELECIONADA: 'Cotação selecionada',
  COMPRADOR_ATRIBUIDO: 'Comprador atribuído',
  GRUPOS_PAGAMENTO_CRIADOS: 'Grupos de pagamento criados',
  PAYMENT_SCHEDULED: 'Pagamento agendado',
  PAYMENT_COMPLETED: 'Pagamento realizado',
  ADVANCE_PAYMENT_COMPLETED: 'Adiantamento realizado',
  MOVE_TO_RECEIPT: 'Movido para recebimento',
  CONFIRM_RECEIVING: 'Recebimento confirmado',
  RECEIVING_PROGRESS: 'Acompanhamento do recebimento',
  OPERATIONAL_RECEIPT_COMPLETED: 'Recebimento operacional concluído',
  ITEM_RECEIVING_REGISTRATION: 'Registo de recebimento de item',
  ITEM_RECEIVING_ADJUSTMENT: 'Ajuste de recebimento de item',
  RECEIVING_REOPENED: 'Recebimento reaberto para correção',
  PAYMENT_DIVERGENCE_DETECTED: 'Divergência de pagamento',
  RECEIVING_LINKAGE_REPAIR: 'Reparo de vínculo de recebimento',
  RECEIVING_PAYMENT_GROUP_SYNC_REPAIR: 'Sincronização de status de recebimento (pagamento)',
  NOTA_FINANCEIRA: 'Nota financeira',
  STATUS_SYNC: 'Sincronização de estado',
};

// A safe normalization for unknown codes: never hide the raw meaning — turn CODE_LIKE_THIS into
// "Code like this" so it is still readable while the raw code stays available as secondary text.
export function normalizeActionCode(code: string): string {
  const cleaned = code.replace(/[_]+/g, ' ').trim().toLowerCase();
  if (cleaned === '') return code;
  return cleaned.charAt(0).toUpperCase() + cleaned.slice(1);
}

export function humanizeActionLabel(code: string | null | undefined): string {
  if (!code) return '—';
  return HISTORY_ACTION_LABELS[code] ?? normalizeActionCode(code);
}

// Known attachment/document categories (real AttachmentConstants codes + a few workflow labels). Unknown
// codes are shown unchanged (§5).
export const DOCUMENT_TYPE_LABELS: Record<string, string> = {
  PROFORMA: 'Proforma',
  QUOTATION: 'Cotação',
  PO: 'P.O.',
  PO_DOCUMENT: 'P.O.',
  PAYMENT_SCHEDULE: 'Cronograma de pagamento',
  PAYMENT_PROOF: 'Comprovativo de Pagamento',
  ADVANCE_PAYMENT_PROOF: 'Comprovativo de adiantamento',
  RECEIPT: 'Recibo do Fornecedor',
  FISCAL_RECEIPT: 'Recibo Fiscal',
  RECEIVING_EVIDENCE: 'Comprovativo de Recebimento/Execução',
  OPERATION_INVOICE: 'Factura da operação',
  CREDIT_NOTE: 'Nota de crédito',
  DEBIT_NOTE: 'Nota de débito',
  INVOICE: 'Fatura',
  PAYMENT_SOURCE_DOCUMENT: 'Documento de origem do pagamento',
};

export function humanizeDocumentType(code: string | null | undefined): string {
  if (!code) return '—';
  return DOCUMENT_TYPE_LABELS[code] ?? code;
}

export interface RequestPrintModel {
  header: {
    requestNumber: string;
    title: string;
    statusName: string;
    companyPlant: string | null;
    generatedAt: string;
    /** Display name of the currently authenticated user who initiated the print (never persisted). */
    printedBy: string | null;
  };
  general: PrintField[];
  description: string | null;
  workflow: PrintField[] | null;
  lineItems: PrintLineItem[];
  groups: PrintGroup[];
  quotations: PrintQuotation[];
  approvals: PrintApproval[];
  attachments: PrintAttachment[];
  history: PrintHistoryEntry[];
}

// Keep a field only when it carries a real value (never "undefined"/"null"/empty).
function field(label: string, value: unknown): PrintField | null {
  if (value === null || value === undefined) return null;
  const s = typeof value === 'string' ? value.trim() : String(value);
  if (s === '' || s.toLowerCase() === 'null' || s.toLowerCase() === 'undefined') return null;
  return { label, value: s };
}
const compact = (fields: (PrintField | null)[]): PrintField[] => fields.filter((f): f is PrintField => f != null);

function money(amount: number | null | undefined, currency?: string | null): string | null {
  if (amount === null || amount === undefined) return null;
  return formatCurrencyAO(amount, currency ?? undefined);
}

function groupTitle(g: RequestPoGroupDto, index: number): string {
  const supplier = g.supplierNameSnapshot?.trim();
  return supplier ? `Lote ${index + 1} — ${supplier}` : `Lote ${index + 1}`;
}

function mapGroup(g: RequestPoGroupDto, index: number): PrintGroup {
  const fields = compact([
    field('Fornecedor', g.supplierNameSnapshot),
    field('NIF', g.supplierNifSnapshot),
    field('Montante', money(g.totalAmount, g.currencyCode)),
    field('Nº P.O.', g.purchaseOrderNumber),
    field('Condição de pagamento', g.paymentConditionCode),
    field('Adiantamento (%)', g.advancePaymentPercent != null ? `${g.advancePaymentPercent}%` : null),
    field('Estado', g.status),
    field('Itens', g.lineItemCount),
    field('Documentos', g.attachmentCount),
  ]);
  const payments = (g.payments ?? []).map(p => compact([
    field('Tipo', p.paymentType),
    field('Estado', p.paymentStatus),
    field('Planeado', money(p.plannedAmount, p.currencyCode)),
    field('Pago', p.actualPaidAmount != null ? money(p.actualPaidAmount, p.currencyCode) : null),
    field('Agendado', p.scheduledDateUtc ? formatDate(p.scheduledDateUtc) : null),
    field('Data de pagamento', p.paidDateUtc ? formatDate(p.paidDateUtc) : null),
    field('Divergência', p.hasDivergence ? 'Sim' : null),
  ]));
  return { title: groupTitle(g, index), fields, payments };
}

function mapLineItem(li: RequestLineItemDto): PrintLineItem {
  return {
    fields: compact([
      field('Nº', (li as any).lineNumber),
      field('Descrição', li.description),
      field('Quantidade', li.quantity),
      field('Unidade', (li as any).unit),
      field('Preço unit.', money((li as any).unitPrice)),
      field('Total', money((li as any).totalPrice ?? (li as any).totalAmount)),
    ]),
  };
}

function mapQuotation(q: SavedQuotationDto): PrintQuotation {
  return {
    selected: !!q.isSelected,
    fields: compact([
      field('Fornecedor', q.supplierNameSnapshot),
      field('Documento', q.documentNumber),
      field('Data', q.documentDate ? formatDate(q.documentDate) : null),
      field('Moeda', q.currency),
      field('Total', money(q.totalAmount, q.currency)),
      field('Itens', q.itemCount),
      field('Selecionada', q.isSelected ? 'Sim' : null),
    ]),
  };
}

function mapApproval(b: ApprovalBatchSummary): PrintApproval {
  return {
    fields: compact([
      field('Lote', b.batchNumber),
      field('Estado', b.status),
      field('Montante aprovado', money(b.approvedTotalAmount ?? null)),
      field('Criado por', b.createdByUserName),
      field('Atualizado por', b.updatedByUserName),
      field('Motivo do reajuste', b.adjustmentReason),
      field('Reajuste solicitado por', b.adjustmentRequestedByName),
    ]),
  };
}

function mapAttachment(a: RequestAttachmentDto): PrintAttachment {
  return {
    voided: !!a.voidedAtUtc,
    fields: compact([
      field('Documento', a.fileName),
      field('Tipo', humanizeDocumentType(a.attachmentTypeCode)),
      field('Enviado em', a.uploadedAtUtc ? formatDate(a.uploadedAtUtc) : null),
      field('Enviado por', a.uploadedByName),
      field('Anulado', a.voidedAtUtc ? 'Sim' : null),
    ]),
  };
}

// History ordered OLDEST → NEWEST for an audit-friendly read; the source array is copied, never sorted
// in place.
function mapHistory(rows: RequestStatusHistoryDto[]): PrintHistoryEntry[] {
  return [...rows]
    .sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime())
    .map(h => ({
      at: formatDateTime(h.createdAtUtc),
      actionLabel: humanizeActionLabel(h.actionTaken),
      actionCode: h.actionTaken || '',
      actor: h.actorName || '—',
      newStatus: h.newStatusName || '',
      comment: h.comment && h.comment.trim() !== '' ? h.comment : null,
    }));
}

function mapWorkflow(projection: any | null): PrintField[] | null {
  if (!projection) return null;
  const next = Array.isArray(projection.nextActions) ? projection.nextActions[0] : null;
  const units = Array.isArray(projection.units) ? projection.units : [];
  const fields = compact([
    next ? field('Próxima ação', next.label) : null,
    next ? field('Responsável', next.responsibleRole ?? next.unitLabel) : null,
    units.length > 1 ? field('Fluxos ativos', String(units.length)) : null,
  ]);
  return fields.length > 0 ? fields : null;
}

// Filename-/title-safe representation of the request number for the Save-as-PDF suggestion. The
// business number (e.g. REQ-07/09/2026-377) is UNCHANGED everywhere else; here "/" and other
// filesystem-unsafe characters become "-". Example title:
//   "Portal Gerencial - Pedido REQ-07-09-2026-377 - 2026-09-14"
export function safeRequestNumber(requestNumber: string | null | undefined): string {
  const raw = (requestNumber ?? '').trim() || 'Pedido';
  return raw.replace(/[\\/?%*:|"<>]/g, '-');
}

export function toPrintFileTitle(requestNumber: string | null | undefined, date: Date = new Date()): string {
  const ymd = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  return `Portal Gerencial - Pedido ${safeRequestNumber(requestNumber)} - ${ymd}`;
}

export function buildRequestPrintModel(detail: RequestDetailsDto, projection: any | null = null, printedByName: string | null = null): RequestPrintModel {
  const companyPlant = [detail.companyName, detail.plantName].filter(Boolean).join(' · ') || null;

  const general = compact([
    field('Nº do pedido', detail.requestNumber),
    field('Tipo', detail.requestTypeName),
    field('Solicitante', detail.requesterName),
    field('Comprador', detail.buyerName),
    field('Departamento', detail.departmentName),
    field('Empresa', detail.companyName),
    field('Planta', detail.plantName),
    field('Nível de necessidade', detail.needLevelName),
    field('Valor estimado', money(detail.estimatedTotalAmount, detail.currencyCode)),
    field('Data de criação', detail.createdAtUtc ? formatDate(detail.createdAtUtc) : null),
    field('Data necessária', detail.needByDateUtc ? formatDate(detail.needByDateUtc) : null),
    field('Estado atual', detail.displayStatusName ?? detail.statusName),
  ]);

  return {
    header: {
      requestNumber: detail.requestNumber || '—',
      title: detail.title || '—',
      statusName: detail.displayStatusName ?? detail.statusName,
      companyPlant,
      generatedAt: formatDateTime(new Date().toISOString()),
      printedBy: printedByName && printedByName.trim() !== '' ? printedByName.trim() : null,
    },
    general,
    description: detail.description && detail.description.trim() !== '' ? detail.description : null,
    workflow: mapWorkflow(projection),
    lineItems: (detail.lineItems ?? []).map(mapLineItem),
    groups: (detail.poGroups ?? []).map(mapGroup),
    quotations: (detail.quotations ?? []).map(mapQuotation),
    approvals: (detail.approvalBatches ?? []).map(mapApproval),
    attachments: (detail.attachments ?? []).map(mapAttachment),
    history: mapHistory(detail.statusHistory ?? []),
  };
}
