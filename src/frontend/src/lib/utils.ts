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
            return {
                responsible: 'Comprador',
                nextAction: 'Corrigir P.O devolvida por Finanças e re-registrar'
            };
        case 'PAYMENT_SCHEDULED':
            return {
                responsible: 'Financeiro',
                nextAction: 'Realizar o pagamento'
            };
        case 'PAYMENT_COMPLETED':
        case 'WAITING_RECEIPT':
            return {
                responsible: 'Recebimento',
                nextAction: 'Confirmar recibo e finalizar pedido'
            };
        case 'IN_FOLLOWUP':
            return {
                responsible: 'RECEBIMENTO',
                nextAction: 'AGUARDAR FINALIZACAO DE RECEBIMENTO'
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
        'QUOTATION_COMPLETED', 'PO_ISSUED', 
        'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED'
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

    const d = new Date(needByDateUtc);
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

export function formatDate(date: string | Date | null | undefined): string {
    if (!date) return '-';
    // If it's a string from backend like "2023-10-25", we want to keep that exact date regardless of timezone
    // new Date("2023-10-25") in some browsers/locales might shift.
    // However, our backend strings are usually ISO UTC
    const d = new Date(date);
    if (isNaN(d.getTime())) return '-';

    const day = d.getUTCDate().toString().padStart(2, '0');
    const month = (d.getUTCMonth() + 1).toString().padStart(2, '0');
    const year = d.getUTCFullYear();

    return `${day}/${month}/${year}`;
}

export function formatDateTime(date: string | Date | null | undefined): string {
    if (!date) return '-';
    const d = new Date(date);
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
    const d = new Date(date);
    if (isNaN(d.getTime())) return '-';

    const hours = d.getUTCHours().toString().padStart(2, '0');
    const minutes = d.getUTCMinutes().toString().padStart(2, '0');

    return `${hours}:${minutes}`;
}

export async function computeFileHash(file: File): Promise<string> {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    return hashHex;
}
