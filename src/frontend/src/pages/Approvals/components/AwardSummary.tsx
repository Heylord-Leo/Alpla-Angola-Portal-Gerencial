import { RequestPoGroupDto, SavedQuotationDto, RequestAttachmentDto } from '../../../types';
import { formatCurrencyAO } from '../../../lib/utils';
import { CheckCircle2, FileText, Building2, CreditCard, DollarSign } from 'lucide-react';

interface AwardSummaryProps {
    poGroups: RequestPoGroupDto[];
    quotations: SavedQuotationDto[];
    attachments: RequestAttachmentDto[];
}

export function AwardSummary({ poGroups, quotations, attachments }: AwardSummaryProps) {
    if (!poGroups || poGroups.length === 0) {
        return (
            <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                Nenhuma cotação vencedora disponível ou grupos não gerados.
            </div>
        );
    }

    const getPaymentConditionLabel = (code?: string | null, advancePercent?: number | null) => {
        if (!code) return 'Não definida';
        if (code === 'ADVANCE_FULL') return '100% Antecipado';
        if (code === 'ADVANCE_PARTIAL') return `Antecipado Parcial (${advancePercent}%)`;
        if (code === 'POST_PAID') return 'Pós-pago';
        return code;
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                <CheckCircle2 size={18} color="var(--color-success)" />
                Resumo da Premiação (Grupos de Compra)
            </h3>
            
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: '16px' }}>
                {poGroups.map(group => {
                    return (
                        <div key={group.id} style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-lg)',
                            padding: '16px',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '12px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <div style={{ width: '32px', height: '32px', borderRadius: '50%', backgroundColor: 'var(--color-bg-subtle)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                        <Building2 size={16} color="var(--color-text-muted)" />
                                    </div>
                                    <div>
                                        <div style={{ fontWeight: 600, fontSize: '0.9rem', color: 'var(--color-text-main)' }}>
                                            {group.supplierNameSnapshot || 'Fornecedor Desconhecido'}
                                        </div>
                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                            {group.lineItemCount} {group.lineItemCount === 1 ? 'item' : 'itens'} aprovado(s)
                                        </div>
                                    </div>
                                </div>
                            </div>
                            
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem' }}>
                                    <DollarSign size={14} color="var(--color-text-muted)" />
                                    <span style={{ color: 'var(--color-text-muted)' }}>Total:</span>
                                    <span style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>
                                        {formatCurrencyAO(group.totalAmount)} {group.currencyCode || 'AKZ'}
                                    </span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem' }}>
                                    <CreditCard size={14} color="var(--color-text-muted)" />
                                    <span style={{ color: 'var(--color-text-muted)' }}>Pagamento:</span>
                                    <span style={{ fontWeight: 500, color: 'var(--color-text-main)' }}>
                                        {getPaymentConditionLabel(group.paymentConditionCode, group.advancePaymentPercent)}
                                    </span>
                                </div>
                            </div>
                        </div>
                    );
                })}
            </div>

            {quotations.length > 0 && (
                <div style={{ marginTop: '16px' }}>
                    <h4 style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', marginBottom: '12px' }}>Documentos Anexados (Cotações)</h4>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {attachments.filter(a => a.attachmentTypeCode === 'QUOTATION').map(att => (
                            <div key={att.id} style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                                <FileText size={16} />
                                <span>{att.fileName}</span>
                            </div>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
}
