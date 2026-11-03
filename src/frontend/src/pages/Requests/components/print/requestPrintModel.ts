import type {
  RequestDetailsDto, RequestPoGroupDto, RequestStatusHistoryDto,
  RequestAttachmentDto, SavedQuotationDto, RequestLineItemDto, ApprovalBatchSummary, ApprovalBatchItemSummary,
} from '../../../../types';
import { formatCurrencyAO, formatCalendarDate, formatDate, formatDateTime } from '../../../../lib/utils';
import { documentTypeLabel, isSelectableDocumentType } from '../../../../lib/sourceDocumentType';
import { describeQuotationLifecycle } from '../../../../lib/quotationLifecycle';

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

export interface PrintAttachment { fields: PrintField[]; voided: boolean; superseded?: boolean; currentPo?: boolean; }

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
  // v2.249.2 — neutral heading: the row's own text carries the stored return kind ("Corrigir P.O." / "Cancelamento
  // solicitado — não corrigir" / unclassified). Never assume correction or cancellation from the action code alone.
  FINANCE_RETURN_ADJUSTMENT: 'Devolvido por Finanças',
  REGISTER_PO: 'P.O. registrada',
  REREGISTER_PO: 'P.O. corrigida e registrada',
  // Phase 1 P.O. cancellation / return to quotation (PoGroupCancellationConstants.HistoryActions)
  PO_GROUP_CANCELLED: 'P.O. cancelada',
  PO_GROUP_ITEMS_RELEASED: 'Itens devolvidos à cotação',
  PO_GROUP_ITEMS_CLOSED: 'Itens encerrados após aprovação',
  PO_CANCELLATION_ECF_DECLARED: 'Declaração do comprador sobre a ECF',
  PO_CANCELLATION_CONFIRMED: 'Anulação da ECF confirmada por Finanças',
  PO_CANCELLATION_REJECTED: 'Declaração da ECF rejeitada por Finanças',
  PO_CANCELLATION_NOTIFICATION_RESENT: 'Notificação de cancelamento reenviada',
  PO_NUMBER_REUSED_AFTER_CANCELLATION: 'Número de P.O. reutilizado após cancelamento',
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
  // v2.245.9 — post-payment completion lifecycle (group-scoped events target the GROUP's state,
  // resolved by the backend; REQUEST_COMPLETED is the request-level transition).
  GRUPO_CLASSIFICADO: 'Documento de origem classificado',
  FISCAL_RECEIPT_UNLOCKED: 'Recibo Fiscal desbloqueado',
  FISCAL_RECEIPT_UPLOADED: 'Recibo Fiscal anexado',
  GROUP_COMPLETED: 'Grupo concluído',
  REQUEST_COMPLETED: 'Pedido finalizado',
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

function mapGroup(g: RequestPoGroupDto, index: number, attachments: RequestAttachmentDto[] = []): PrintGroup {
  const currentPoFileName = g.currentPoAttachmentId ? attachments.find(a => a.id === g.currentPoAttachmentId)?.fileName : undefined;
  const fields = compact([
    field('Fornecedor', g.supplierNameSnapshot),
    field('NIF', g.supplierNifSnapshot),
    field('Montante', money(g.totalAmount, g.currencyCode)),
    field('Nº P.O.', g.purchaseOrderNumber),
    field('Condição de pagamento', g.paymentConditionCode),
    field('Adiantamento (%)', g.advancePaymentPercent != null ? `${g.advancePaymentPercent}%` : null),
    // v2.245.9: the group's OPERATIONAL classification (authoritative), per group; omitted when the
    // group was never classified — never a fabricated "Não classificado".
    field('Documento de origem', isSelectableDocumentType(g.sourceDocumentType) ? documentTypeLabel(g.sourceDocumentType) : null),
    field('Estado', g.status),
    // v2.249.4 — a cancelled group (RESTART_QUOTATION) keeps no live lines: print what it covered, as history.
    ...(g.status === 'CANCELLED' && g.lineItemCount === 0 && (g.historicalLineItemCount ?? 0) > 0
      ? [field('Itens (histórico, libertados)', g.historicalLineItemCount)]
      : [field(g.status === 'CANCELLED' ? 'Itens (histórico)' : 'Itens', g.lineItemCount)]),
    field('Documentos', g.attachmentCount),
    field('Documento P.O. atual', currentPoFileName ?? null),
  ]);
  const payments = (g.payments ?? []).map(p => compact([
    field('Tipo', p.paymentType),
    field('Estado', p.paymentStatus),
    field('Planeado', money(p.plannedAmount, p.currencyCode)),
    field('Pago', p.actualPaidAmount != null ? money(p.actualPaidAmount, p.currencyCode) : null),
    field('Agendado', p.scheduledDateUtc ? formatCalendarDate(p.scheduledDateUtc) : null),   // calendar date (business), v2.249.4
    field('Data de pagamento', p.paidDateUtc ? formatCalendarDate(p.paidDateUtc) : null),
    field('Divergência', p.hasDivergence ? 'Sim' : null),
  ]));
  return { title: groupTitle(g, index), fields, payments };
}

// ── v2.249.8 — line values: the requester's estimate vs the approved value ──
//
// Finding of the manual TEST validation of v2.249.7 (REQ-03/11/2026-630): the "Itens" section printed "Preço unit. 0,00 / Total
// 0,00" for both lines while the quotation, the batch and the P.O. group carried 61.560 AOA. Those two fields are the REQUEST
// LINE's own `unitPrice` / `totalAmount` — what the requester typed at creation (a QUOTATION request is created with 0: the
// requester asks for a quotation, no price is requested of them) — never the approved price: nothing in the approval or P.O.
// flow writes them (LineItemFactory / the requester's line edit are the only writers). The approved value of a line lives in the
// APPROVED batch's winning candidate snapshot (frozen at submission, own currency), or, for a legacy pre-candidate batch, in the
// Buyer-selected quotation item. The print now (a) labels the requester's figures as an estimate and omits a 0 placeholder, and
// (b) prints the approved values per line from that authoritative source: latest APPROVED batch holding the line, released items
// excluded (a P.O.-group cancellation that returned the line to quotation), lines closed by a CANCEL_ITEMS cancellation marked
// as historical, each line in its candidate's currency — never a group total divided over lines, never a cross-currency sum.
export interface ApprovedLineValue {
  batchNumber: number;
  supplierName: string | null;
  unitPrice: number;
  lineTotal: number;
  currency: string | null;
  /** CANDIDATE = frozen ApprovalBatchItemCandidate snapshot; LEGACY_QUOTATION_ITEM = pre-candidate batch (Buyer-selected quotation item). */
  source: 'CANDIDATE' | 'LEGACY_QUOTATION_ITEM';
}

function batchItems(b: ApprovalBatchSummary): ApprovalBatchItemSummary[] {
  return Array.isArray(b.items) ? b.items : [];
}

/** The approved value of every line that an APPROVED batch still holds (released items excluded), keyed by request line id. */
export function resolveApprovedLineValues(detail: RequestDetailsDto): Map<string, ApprovedLineValue> {
  const out = new Map<string, ApprovedLineValue>();
  const approved = (detail.approvalBatches ?? [])
    .filter(b => b.status === 'APPROVED')
    .slice()
    .sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime());   // latest first
  for (const b of approved) {
    for (const item of batchItems(b)) {
      if (!item.requestLineItemId || out.has(item.requestLineItemId)) continue;
      if (item.releasedAtUtc) continue;   // released by a P.O.-group cancellation: the line is back in quotation, this value is history
      const candidates = Array.isArray(item.candidates) ? item.candidates : [];
      const winner = (item.selectedCandidateId ? candidates.find(c => c.id === item.selectedCandidateId) : undefined) ?? candidates.find(c => c.isWinner);
      if (winner) {
        out.set(item.requestLineItemId, { batchNumber: b.batchNumber, supplierName: winner.supplierName ?? null, unitPrice: winner.unitPrice, lineTotal: winner.lineTotal, currency: winner.currency ?? null, source: 'CANDIDATE' });
        continue;
      }
      if (candidates.length === 0 && item.selectedQuotationItemId) {
        for (const q of detail.quotations ?? []) {
          const qi = (q.items ?? []).find(x => x.id === item.selectedQuotationItemId);
          if (!qi) continue;
          out.set(item.requestLineItemId, { batchNumber: b.batchNumber, supplierName: q.supplierNameSnapshot ?? null, unitPrice: qi.unitPrice, lineTotal: qi.lineTotal, currency: q.currency ?? null, source: 'LEGACY_QUOTATION_ITEM' });
          break;
        }
      }
      // An approved item without a resolvable winner prints no approved value (never a fabricated one).
    }
  }
  return out;
}

function positive(value: unknown): number | null {
  return typeof value === 'number' && value > 0 ? value : null;
}

function mapLineItem(li: RequestLineItemDto, approved: ApprovedLineValue | undefined, requestCurrency: string | null | undefined): PrintLineItem {
  const estimateCurrency = li.currencyCode ?? requestCurrency ?? undefined;
  const estimatedUnit = positive((li as any).unitPrice);
  const estimatedTotal = positive((li as any).totalPrice ?? (li as any).totalAmount);
  const historical = li.quotationLifecycleStatus === 'CANCELLED_AFTER_APPROVAL';
  const approvedSuffix = historical ? ' (histórico — P.O. cancelada)' : '';
  return {
    fields: compact([
      field('Nº', (li as any).lineNumber),
      field('Descrição', li.description),
      field('Quantidade', li.quantity),
      field('Unidade', (li as any).unit),
      // The requester's own figures, labelled as such and only when given — a 0 placeholder is not a price.
      field('Preço unit. (estimativa do solicitante)', estimatedUnit != null ? money(estimatedUnit, estimateCurrency) : null),
      field('Total (estimativa do solicitante)', estimatedTotal != null ? money(estimatedTotal, estimateCurrency) : null),
      // The approved value, from the winning candidate snapshot of the APPROVED batch, in its own currency.
      field(`Preço unit. aprovado${approvedSuffix}`, approved ? money(approved.unitPrice, approved.currency) : null),
      field(`Total aprovado (c/ IVA)${approvedSuffix}`, approved ? money(approved.lineTotal, approved.currency) : null),
      field('Fornecedor aprovado', approved?.supplierName ?? null),
      field('Lote de aprovação', approved ? `#${approved.batchNumber}` : null),
      // Neither an estimate nor an approved value: say so, instead of printing 0,00 as if the purchase cost nothing.
      field('Valores', !approved && estimatedUnit == null && estimatedTotal == null ? 'Sem estimativa do solicitante; sem valor aprovado para esta linha' : null),
    ]),
  };
}

function mapQuotation(q: SavedQuotationDto): PrintQuotation {
  return {
    selected: !!q.isSelected,
    fields: compact([
      field('Fornecedor', q.supplierNameSnapshot),
      field('Documento', q.documentNumber),
      field('Data', q.documentDate ? formatCalendarDate(q.documentDate) : null),   // the quotation's own date: calendar, never shifted
      field('Moeda', q.currency),
      field('Total', money(q.totalAmount, q.currency)),
      // v2.249.4 — "Itens" = quoted lines; NOT_QUOTED/IGNORED placeholders are reported separately, never as purchases.
      field('Itens cotados', q.quotedItemCount ?? q.itemCount),
      field('Itens não cotados', (q.notQuotedItemCount ?? 0) > 0 ? q.notQuotedItemCount : null),
      field('Selecionada', q.isSelected ? 'Sim' : null),
      // v2.249.5 — lifecycle context from the server projection (e.g. "Histórica — adjudicação cancelada"), never inferred.
      field('Situação', quotationSituation(q)),
    ]),
  };
}

function quotationSituation(q: SavedQuotationDto): string | null {
  const d = describeQuotationLifecycle(q);
  if (!d) return null;
  return `${d.label}. ${d.detail}`;
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
    superseded: !a.voidedAtUtc && !!a.supersededAtUtc,
    currentPo: a.isCurrentPo === true,
    fields: compact([
      field('Documento', a.fileName),
      field('Tipo', humanizeDocumentType(a.attachmentTypeCode)),
      field('Enviado em', a.uploadedAtUtc ? formatDate(a.uploadedAtUtc) : null),
      field('Enviado por', a.uploadedByName),
      field('Anulado', a.voidedAtUtc ? 'Sim' : null),
      // v2.249.4 — replacement ≠ cancellation: a superseded P.O. file is history, the current one is named as such.
      field('Situação', a.isCurrentPo ? 'P.O. atual' : (!a.voidedAtUtc && a.supersededAtUtc) ? `Substituído em ${formatDate(a.supersededAtUtc)} (histórico)` : null),
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
    field('Data necessária', detail.needByDateUtc ? formatCalendarDate(detail.needByDateUtc) : null),
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
    lineItems: (() => { const approved = resolveApprovedLineValues(detail); return (detail.lineItems ?? []).map(li => mapLineItem(li, approved.get(li.id), detail.currencyCode)); })(),
    groups: (detail.poGroups ?? []).map((g, i) => mapGroup(g, i, detail.attachments ?? [])),
    quotations: (detail.quotations ?? []).map(mapQuotation),
    approvals: (detail.approvalBatches ?? []).map(mapApproval),
    attachments: (detail.attachments ?? []).map(mapAttachment),
    history: mapHistory(detail.statusHistory ?? []),
  };
}
