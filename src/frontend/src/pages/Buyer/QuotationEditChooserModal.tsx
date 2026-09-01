import React from 'react';
import { X, FileEdit } from 'lucide-react';
import type { SavedQuotationDto } from '../../types';

interface Props {
    quotations: SavedQuotationDto[];
    onSelect: (q: SavedQuotationDto) => void;
    onClose: () => void;
}

const fmtTotal = (q: SavedQuotationDto) => {
    const n = (q.totalAmount ?? 0).toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return q.currency ? `${n} ${q.currency}` : n;
};

/**
 * Adjustment V2 Phase 4 — read-only picker shown when a batch is composed from MORE THAN ONE existing
 * quotation. Lists each contributing quotation with business-readable data (supplier, document,
 * total, item count) and opens the chosen one in EDIT mode. No GUIDs, no editing here beyond the
 * selection. Not a quotation-management page.
 */
export const QuotationEditChooserModal: React.FC<Props> = ({ quotations, onSelect, onClose }) => {
    return (
        <div
            role="dialog"
            aria-modal="true"
            onClick={onClose}
            style={{ position: 'fixed', inset: 0, background: 'rgba(17,24,39,0.7)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 10001, padding: 20 }}
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{ background: '#FFFFFF', border: '1px solid var(--color-border)', borderRadius: 12, width: '100%', maxWidth: 560, maxHeight: '85vh', overflowY: 'auto', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}
            >
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '18px 20px', borderBottom: '1px solid var(--color-border)' }}>
                    <div>
                        <h2 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 800, color: 'var(--color-primary)' }}>Revisar cotação</h2>
                        <p style={{ margin: '2px 0 0', fontSize: '0.8125rem', color: 'var(--color-text-muted)' }}>
                            Este lote foi composto por mais de uma cotação. Escolha qual deseja revisar (será criada uma nova revisão).
                        </p>
                    </div>
                    <button onClick={onClose} aria-label="Fechar" style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', padding: 4 }}>
                        <X size={20} />
                    </button>
                </div>

                <div style={{ padding: '14px 20px', display: 'flex', flexDirection: 'column', gap: 10 }}>
                    {quotations.map(q => (
                        <button
                            key={q.id}
                            type="button"
                            onClick={() => onSelect(q)}
                            title="Revisar esta cotação"
                            style={{ textAlign: 'left', width: '100%', cursor: 'pointer', background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 10, padding: '12px 14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}
                        >
                            <div style={{ minWidth: 0 }}>
                                <div style={{ fontWeight: 700, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {q.supplierNameSnapshot || 'Fornecedor'}
                                </div>
                                <div style={{ fontSize: '0.76rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                                    {q.documentNumber ? `Documento ${q.documentNumber}` : 'Sem número de documento'}
                                    {' · '}{(q.items || []).length} {(q.items || []).length === 1 ? 'item' : 'itens'}
                                </div>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0 }}>
                                <span style={{ fontWeight: 700, color: 'var(--color-primary)', whiteSpace: 'nowrap' }}>{fmtTotal(q)}</span>
                                <FileEdit size={16} color="var(--color-primary)" />
                            </div>
                        </button>
                    ))}
                </div>
            </div>
        </div>
    );
};
