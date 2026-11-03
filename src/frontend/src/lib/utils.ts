export function formatCurrencyAO(amount: number | null | undefined, currencyCode?: string): string {
    if (amount === null || amount === undefined || isNaN(amount)) {
        return currencyCode ? `${currencyCode} 0,00` : '0,00';
    }

    // Uses pt-AO explicitly to ensure '.' for thousands and ',' for decimal
    const formatted = new Intl.NumberFormat('pt-AO', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    }).format(amount);

    return currencyCode ? `${currencyCode} ${formatted}` : formatted;
}

export interface RequestGuidance {
    responsible: string;
    nextAction: string;
}

export function getRequestGuidance(statusCode: string, requestTypeCode: string | null): RequestGuidance {
    const isPayment = requestTypeCode === 'PAYMENT';

    switch (statusCode) {
        case 'DRAFT':
            return {
                responsible: 'Solicitante',
                nextAction: 'Completar e submeter o pedido'
            };
        case 'WAITING_AREA_APPROVAL':
            return {
                responsible: 'Aprovador da Área',
                nextAction: isPayment ? 'Aguardar decisão da área' : 'Selecionar vencedor e aprovar'
            };
        case 'AREA_ADJUSTMENT':
            return {
                responsible: isPayment ? 'Solicitante' : 'Comprador',
                nextAction: isPayment
                    ? 'Revisar o pedido e reenviar para aprovação da área'
                    : 'Revisar o pedido e reenviar para a próxima etapa'
            };
        case 'WAITING_FINAL_APPROVAL':
            return {
                responsible: 'Aprovador Final',
                nextAction: 'Aguardar decisão da aprovação final'
            };
        case 'FINAL_ADJUSTMENT':
            return {
                responsible: isPayment ? 'Solicitante' : 'Comprador',
                nextAction: isPayment
                    ? 'Revisar o pedido e reenviar para aprovação final'
                    : 'Revisar o pedido e reenviar para a próxima etapa'
            };
        case 'APPROVED':
            return {
                responsible: 'Comprador',
                nextAction: 'Prosseguir com a emissão ou inserção da P.O'
            };
        case 'PO_ISSUED':
            return {
                responsible: 'Financeiro',
                nextAction: 'Pagar ou agendar o pagamento'
            };
        case 'WAITING_PO_CORRECTION':
            // v2.249.2 — scalar-only fallback (no group projection → the return kind is unknown here): neutral wording.
            // The server projection (RequestWorkflowProjectionBuilder) names the stored kind per group.
            return {
                responsible: 'Comprador',
                nextAction: 'Rever a P.O devolvida por Finanças (corrigir ou cancelar)'
            };
        case 'PAYMENT_SCHEDULED':
            return {
                responsible: 'Financeiro',
                nextAction: 'Realizar o pagamento'
            };
        case 'PAYMENT_COMPLETED':
            return {
                responsible: 'Recebimento',
                nextAction: 'Mover para fase de recebimento e conferir itens'
            };
        case 'WAITING_RECEIPT':
            return {
                responsible: 'Financeiro',
                nextAction: 'Anexar recibo do fornecedor e finalizar pedido'
            };
        case 'WAITING_FISCAL_RECEIPT':
            return {
                responsible: 'Financeiro',
                nextAction: 'Registrar o Recibo Fiscal para concluir o grupo'
            };
        case 'IN_FOLLOWUP':
            return {
                responsible: 'Recebimento',
                nextAction: 'Resolver itens pendentes e confirmar recebimento'
            };
        case 'WAITING_QUOTATION':
            return {
                responsible: 'Comprador',
                nextAction: 'Realizar a cotação conforme solicitado'
            };
        case 'QUOTATION_ADJUSTMENT':
            return {
                responsible: 'Comprador',
                nextAction: 'Revisar e reenviar a cotação'
            };
        case 'QUOTATION_COMPLETED':
            return {
                responsible: 'Comprador',
                nextAction: 'Registrar P.O. para os grupos aprovados'
            };
        case 'PO_REQUESTED':
            return {
                responsible: 'Comprador',
                nextAction: 'Registrar P.O.'
            };
        case 'COMPLETED':
            return {
                responsible: 'Sem ação',
                nextAction: 'Pedido encerrado'
            };
        case 'REJECTED':
            return {
                responsible: 'Sem ação',
                nextAction: 'Pedido encerrado'
            };
        case 'CANCELLED':
            return {
                responsible: 'Sem ação',
                nextAction: 'Pedido cancelado'
            };
        case 'ADVANCE_PAYMENT_REQUIRED':
            return {
                responsible: 'Financeiro',
                nextAction: 'Agendar e processar adiantamento ao fornecedor'
            };
        // v2.245.11: explicit, truthful fallback for a scheduled advance (same strings as the backend
        // RequestWorkflowProjectionBuilder GroupGuidance) — the status previously fell through to the
        // generic "Não definido / Aguardar atualização do sistema" default.
        case 'ADVANCE_PAYMENT_SCHEDULED':
            return {
                responsible: 'Financeiro',
                nextAction: 'Confirmar o pagamento do adiantamento'
            };
        case 'ADVANCE_PAYMENT_COMPLETED':
            return {
                responsible: 'Recebimento / Comprador',
                nextAction: 'Aguardar entrega do fornecedor'
            };
        case 'WAITING_SUPPLIER_DELIVERY':
            return {
                responsible: 'Recebimento / Comprador',
                nextAction: 'Confirmar entrega do bem ou serviço'
            };
        case 'WAITING_RECONCILIATION':
            return {
                responsible: 'Financeiro',
                nextAction: 'Reconciliar facturas e finalizar pagamento remanescente'
            };
        default:
            return {
                responsible: 'Não definido',
                nextAction: 'Aguardar atualização do sistema'
            };
    }
}

export interface UrgencyStyle {
    backgroundColor?: string;
    indicatorColor: string;
    description: string;
    priority: number; // 0: Normal, 1: Soon, 2: Today, 3: Overdue
}

export function isFinalizedStatus(statusCode: string | null | undefined): boolean {
    if (!statusCode) return false;
    const finalized = [
        'COMPLETED', 'REJECTED', 'CANCELLED',
        'PO_ISSUED',
        'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED',
        'WAITING_RECEIPT',
        'ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_COMPLETED',
        'WAITING_SUPPLIER_DELIVERY', 'WAITING_RECONCILIATION'
    ];
    return finalized.includes(statusCode);
}

export function getUrgencyStyle(needByDateUtc: string | null | undefined, statusCode?: string | null): UrgencyStyle | null {
    if (!needByDateUtc) return null;

    // Finalized requests do not get urgency highlighting or priority
    if (isFinalizedStatus(statusCode)) return null;

    const now = new Date();
    // Use UTC for "today" to match deadline normalization
    const today = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());

    const d = parseBackendInstant(needByDateUtc); // offsetless backend string = UTC midnight, never local
    if (isNaN(d.getTime())) return null;
    // Normalized deadline to UTC 00:00:00
    const deadline = Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());

    const diffTime = deadline - today;
    const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));

    if (diffDays < 0) {
        return {
            backgroundColor: '#fee2e2', // light red (rose-100)
            indicatorColor: '#ef4444', // vibrant red (rose-500)
            description: 'ESTE PEDIDO ESTÁ VENCIDO',
            priority: 3
        };
    } else if (diffDays === 0) {
        return {
            backgroundColor: '#ffedd5', // light orange (orange-100)
            indicatorColor: '#f97316', // vibrant orange (orange-500)
            description: 'ESTE PEDIDO VENCE HOJE',
            priority: 2
        };
    } else if (diffDays <= 3) {
        return {
            backgroundColor: '#fef9c3', // light yellow (yellow-100)
            indicatorColor: '#eab308', // vibrant yellow (yellow-500)
            description: 'ESTE PEDIDO VENCE EM BREVE',
            priority: 1
        };
    }

    return null; // Comfortable future
}

/**
 * Parses a backend date/datetime value as the UTC instant it represents.
 *
 * Contract: every `*Utc` column and every date-only business field (`NeedByDateUtc`, `DocumentDate`, `DueDate`,
 * `ScheduledDateUtc`, …) is stored as UTC in `datetime2` and serialized by System.Text.Json WITHOUT an offset
 * (`DateTimeKind.Unspecified` → `"2026-11-04T00:00:00"`). `new Date("2026-11-04T00:00:00")` reads such a string as
 * browser-LOCAL time; combined with UTC getters that shifted the calendar day by one on any UTC+ browser (Angola, UTC+1:
 * 04/11 shown as 03/11 in the Print View — v2.249.4). An offsetless string is therefore read as UTC, a string with an offset
 * as-is, and a bare `YYYY-MM-DD` as UTC midnight (the ECMAScript date-only form is already UTC; no suffix is appended to it).
 */
export function parseBackendInstant(value: string | Date): Date {
    if (value instanceof Date) return value;
    const s = value.trim();
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return new Date(`${s}T00:00:00Z`);
    return new Date(HAS_TIMEZONE.test(s) ? s : `${s}Z`);
}

/**
 * CALENDAR date of a DATE-ONLY business field (NeedByDateUtc, Quotation.DocumentDate, ScheduledDateUtc, PaidDateUtc, invoice
 * DocumentDate/DueDate): pure string slicing, no Date object, no timezone — the day printed is the day stored, on every
 * browser. Use it for calendar dates; use formatDate / formatDateTime for instants (audit timestamps), which are UTC.
 */
export function formatCalendarDate(value: string | Date | null | undefined): string {
    if (!value) return '-';
    if (value instanceof Date) return isNaN(value.getTime()) ? '-' : formatDate(value);
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value.trim());
    if (!match) return '-';
    return `${match[3]}/${match[2]}/${match[1]}`;
}

export function formatDate(date: string | Date | null | undefined): string {
    if (!date) return '-';
    // Calendar day of the UTC instant: offsetless backend strings are UTC (see parseBackendInstant) — never local time.
    const d = parseBackendInstant(date);
    if (isNaN(d.getTime())) return '-';

    const day = d.getUTCDate().toString().padStart(2, '0');
    const month = (d.getUTCMonth() + 1).toString().padStart(2, '0');
    const year = d.getUTCFullYear();

    return `${day}/${month}/${year}`;
}

export function formatDateTime(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = parseBackendInstant(date);
    if (isNaN(d.getTime())) return '-';

    const day = d.getUTCDate().toString().padStart(2, '0');
    const month = (d.getUTCMonth() + 1).toString().padStart(2, '0');
    const year = d.getUTCFullYear();
    const hours = d.getUTCHours().toString().padStart(2, '0');
    const minutes = d.getUTCMinutes().toString().padStart(2, '0');

    return `${day}/${month}/${year} ${hours}:${minutes}`;
}

export function formatTime(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = parseBackendInstant(date);
    if (isNaN(d.getTime())) return '-';

    const hours = d.getUTCHours().toString().padStart(2, '0');
    const minutes = d.getUTCMinutes().toString().padStart(2, '0');

    return `${hours}:${minutes}`;
}

// ---------------------------------------------------------------------------
// Angola timezone-aware formatters (Phase B1 — timezone remediation)
// ---------------------------------------------------------------------------

/** Matches ISO 8601 timezone suffix: Z, +HH:MM, -HH:MM */
const HAS_TIMEZONE = /(Z|[+-]\d{2}:\d{2})$/i;

/** Angola timezone identifier (WAT — West Africa Time, UTC+1) */
const ANGOLA_TZ = 'Africa/Luanda';

/**
 * Parses a backend datetime string as a UTC instant.
 *
 * If the string already contains a timezone suffix (Z or +00:00), it is parsed
 * as-is. If it lacks a suffix (legacy serialization from DateTimeKind.Unspecified),
 * 'Z' is appended to treat the value as UTC — matching the backend contract that
 * all *Utc columns store UTC values.
 *
 * Handles seven-digit fractional seconds from SQL Server datetime2.
 */
function parseUtcInstant(value: string | Date): Date {
    return parseBackendInstant(value);
}

/**
 * Formats a UTC instant as Angola local date (dd/MM/yyyy).
 * Uses Intl.DateTimeFormat — timezone-correct regardless of browser locale.
 */
export function formatDateAngola(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = parseUtcInstant(date);
    if (isNaN(d.getTime())) return '-';
    return new Intl.DateTimeFormat('pt-AO', {
        timeZone: ANGOLA_TZ,
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
    }).format(d);
}

/**
 * Formats a UTC instant as Angola local time (HH:mm).
 * Uses Intl.DateTimeFormat — timezone-correct regardless of browser locale.
 */
export function formatTimeAngola(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = parseUtcInstant(date);
    if (isNaN(d.getTime())) return '-';
    return new Intl.DateTimeFormat('pt-AO', {
        timeZone: ANGOLA_TZ,
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
    }).format(d);
}

/**
 * Formats a UTC instant as Angola local date+time (dd/MM/yyyy HH:mm).
 * Uses Intl.DateTimeFormat — timezone-correct regardless of browser locale.
 */
export function formatDateTimeAngola(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = parseUtcInstant(date);
    if (isNaN(d.getTime())) return '-';
    return new Intl.DateTimeFormat('pt-AO', {
        timeZone: ANGOLA_TZ,
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
    }).format(d);
}

export async function computeFileHash(file: File): Promise<string> {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    return hashHex;
}
