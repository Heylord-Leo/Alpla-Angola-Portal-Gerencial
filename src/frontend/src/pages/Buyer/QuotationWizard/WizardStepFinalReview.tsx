import React from 'react';
import { OcrDraft } from '../../../types';
import { QuotationValidationResult } from './hooks/useQuotationValidation';
import { XCircle, AlertTriangle, FileText } from 'lucide-react';
import { UseQuotationWizardStateReturn } from './hooks/useQuotationWizardState';

interface WizardStepFinalReviewProps {
    draft: OcrDraft | null;
    validation: QuotationValidationResult;
    wizardState: UseQuotationWizardStateReturn;
}

export const WizardStepFinalReview: React.FC<WizardStepFinalReviewProps> = ({ draft, validation, wizardState }) => {
    if (!draft) return null;

    const isSupplierDraft = draft.supplierId && draft.supplierRegistrationStatus === 'DRAFT';
    const isSupplierPendingValidation = draft.supplierId && draft.supplierRegistrationStatus !== 'DRAFT' && !draft.supplierPrimaveraCode;

    // Reconciliation Summary Counts
    const mappedCount = draft.items.filter(i => i.reconciliationStatus === 'MAPPED').length;
    const substituteCount = draft.items.filter(i => i.reconciliationStatus === 'SUBSTITUTE').length;
    const extraCount = draft.items.filter(i => i.reconciliationStatus === 'EXTRA_ITEM').length;
    const ignoredCount = draft.items.filter(i => i.reconciliationStatus === 'IGNORED').length;
    const notQuotedCount = draft.items.filter(i => i.reconciliationStatus === 'NOT_QUOTED').length;

    // Dynamic warnings collection
    const warnings: string[] = [];
    if (isSupplierDraft) {
        warnings.push("Este fornecedor foi criado como rascunho e precisa ser completado/validado em Contratos → Fichas de Fornecedor antes da emissão da P.O.");
    } else if (isSupplierPendingValidation) {
        warnings.push("Este fornecedor existe no portal, mas ainda precisa ser completado/validado antes da emissão da P.O.");
    }
    if (ignoredCount > 0) {
        warnings.push(`Você ignorou ${ignoredCount} linha(s) do documento. Estas não entrarão na cotação final.`);
    }
    if (notQuotedCount > 0) {
        warnings.push(`Existem ${notQuotedCount} item(ns) solicitado(s) que não foram cotados pelo fornecedor.`);
    }
    if (substituteCount > 0 || extraCount > 0) {
        warnings.push(`Foram adicionados itens substitutos ou extras. Eles passarão pela aprovação técnica.`);
    }

    // Financial calculations explicitly including valid statuses
    let totalExcludingIgnored = 0;
    draft.items.forEach(item => {
        if (['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM'].includes(item.reconciliationStatus as string)) {
            totalExcludingIgnored += (item.totalPrice || 0);
        }
    });

    const displayCurrency = draft.currency || 'AOA';
    const formatter = new Intl.NumberFormat('pt-AO', { style: 'currency', currency: displayCurrency });

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', paddingBottom: '32px' }}>
            
            {/* 1. Documento da Cotação */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <FileText size={18} /> Documento da Cotação
                </h3>
                
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '16px', backgroundColor: '#f8fafc', padding: '16px', borderRadius: '8px', border: '1px solid var(--color-border)' }}>
                    <div>
                        <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Nº Documento</p>
                        <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{draft.documentNumber || 'Não especificado'}</p>
                    </div>
                    <div>
                        <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Data Emissão</p>
                        <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{draft.documentDate || 'Não especificada'}</p>
                    </div>
                    <div>
                        <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Data Validade</p>
                        <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{draft.dueDate || 'Não especificada'}</p>
                    </div>
                    <div>
                        <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Moeda</p>
                        <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{draft.currency || 'AOA'}</p>
                    </div>
                </div>
            </div>

            {/* 2. Resumo dos Itens & 3. Resumo Financeiro */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0 }}>Resumo dos Itens</h3>
                    
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', backgroundColor: '#fff', padding: '16px', borderRadius: '8px', border: '1px solid var(--color-border)' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                            <span style={{ color: 'var(--color-text-main)', fontWeight: 500 }}>Corresponde ao item:</span>
                            <span style={{ fontWeight: 600, color: 'var(--color-status-success-text)' }}>{mappedCount}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                            <span style={{ color: 'var(--color-text-main)', fontWeight: 500 }}>Item substituto:</span>
                            <span style={{ fontWeight: 600, color: 'var(--color-status-warning-text)' }}>{substituteCount}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                            <span style={{ color: 'var(--color-text-main)', fontWeight: 500 }}>Item adicional:</span>
                            <span style={{ fontWeight: 600, color: 'var(--color-status-info-text)' }}>{extraCount}</span>
                        </div>
                        <div style={{ height: '1px', backgroundColor: 'var(--color-border)', margin: '4px 0' }}></div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                            <span style={{ color: 'var(--color-text-muted)', fontWeight: 500 }}>Ignorar linha (Sem custo):</span>
                            <span style={{ fontWeight: 600, color: 'var(--color-text-muted)' }}>{ignoredCount}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                            <span style={{ color: 'var(--color-text-muted)', fontWeight: 500 }}>Não cotado:</span>
                            <span style={{ fontWeight: 600, color: 'var(--color-status-error-text)' }}>{notQuotedCount}</span>
                        </div>
                    </div>
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0 }}>Resumo Financeiro</h3>
                    
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', backgroundColor: '#eff6ff', padding: '16px', borderRadius: '8px', border: '1px solid #bfdbfe' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem', color: '#1e40af' }}>
                            <span style={{ fontWeight: 500 }}>Total (Documento OCR):</span>
                            <span>{formatter.format(draft.ocrTotalAmount || 0)}</span>
                        </div>
                        <div style={{ height: '1px', backgroundColor: '#bfdbfe', margin: '4px 0' }}></div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem', color: '#1e3a8a' }}>
                            <span style={{ fontWeight: 700 }}>Total Final Considerado:</span>
                            <span style={{ fontWeight: 700, fontSize: '1.25rem' }}>{formatter.format(totalExcludingIgnored)}</span>
                        </div>
                        <p style={{ fontSize: '0.75rem', color: '#3b82f6', margin: '4px 0 0 0' }}>* Excluindo valores de itens marcados como ignorados e não cotados.</p>
                    </div>
                </div>
            </div>

            {/* List Substitutes and Extras specifically so buyer confirms justifications */}
            {(substituteCount > 0 || extraCount > 0) && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <h4 style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0 }}>Justificativas de Substituição / Adição</h4>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {draft.items.map((item, idx) => {
                            if (item.reconciliationStatus === 'SUBSTITUTE' || item.reconciliationStatus === 'EXTRA_ITEM') {
                                return (
                                    <div key={idx} style={{ backgroundColor: '#fff', padding: '12px', borderRadius: '6px', border: '1px solid var(--color-border)', fontSize: '0.875rem' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                            <span style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>Linha {item.lineNumber}: {item.description}</span>
                                            <span style={{ fontSize: '0.75rem', padding: '2px 6px', borderRadius: '4px', backgroundColor: item.reconciliationStatus === 'SUBSTITUTE' ? 'var(--color-status-warning-light)' : 'var(--color-status-info-light)', color: item.reconciliationStatus === 'SUBSTITUTE' ? 'var(--color-status-warning-text)' : 'var(--color-status-info-text)', fontWeight: 600 }}>
                                                {item.reconciliationStatus === 'SUBSTITUTE' ? 'Substituto' : 'Adicional'}
                                            </span>
                                        </div>
                                        <p style={{ margin: 0, color: 'var(--color-text-muted)' }}>
                                            <strong style={{ color: 'var(--color-text-main)' }}>Justificativa:</strong> {item.reconciliationJustification || 'Não preenchida.'}
                                        </p>
                                    </div>
                                );
                            }
                            return null;
                        })}
                    </div>
                </div>
            )}

            {/* 4. Pontos de Atenção */}
            {warnings.length > 0 && (
                <div style={{ backgroundColor: '#fffbeb', padding: '16px', borderRadius: '8px', border: '1px solid #fcd34d' }}>
                    <div style={{ display: 'flex', alignItems: 'center', color: '#b45309', marginBottom: '12px' }}>
                        <AlertTriangle size={20} style={{ marginRight: '8px' }} />
                        <span style={{ fontSize: '0.875rem', fontWeight: 700 }}>Pontos de Atenção</span>
                    </div>
                    <ul style={{ listStyleType: 'none', padding: 0, fontSize: '0.875rem', color: '#92400e', margin: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {warnings.map((warn, i) => (
                            <li key={i} style={{ display: 'flex', gap: '8px', alignItems: 'flex-start' }}>
                                <span style={{ color: '#d97706' }}>•</span>
                                <span>{warn}</span>
                            </li>
                        ))}
                    </ul>
                </div>
            )}

            {/* Validation Checklist Fallback */}
            {!validation.isValid && (
                <div style={{ backgroundColor: '#fef2f2', padding: '16px', borderRadius: '8px', border: '1px solid #fecaca' }}>
                    <div style={{ display: 'flex', alignItems: 'center', color: '#991b1b', marginBottom: '8px' }}>
                        <XCircle size={20} style={{ marginRight: '8px' }} />
                        <span style={{ fontSize: '0.875rem', fontWeight: 700 }}>Pendências de Validação impeditivas:</span>
                    </div>
                    <ul style={{ listStyleType: 'none', padding: 0, fontSize: '0.875rem', color: '#b91c1c', margin: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {validation.errors.map((err, i) => (
                            <li key={i} style={{ display: 'flex', gap: '8px', alignItems: 'flex-start' }}>
                                <span style={{ color: '#ef4444' }}>•</span>
                                <span>{err}</span>
                            </li>
                        ))}
                    </ul>
                </div>
            )}

            {/* 5. Confirmação Final */}
            <div 
                style={{ 
                    backgroundColor: wizardState.isFinalReviewConfirmed ? '#f0fdf4' : '#f8fafc', 
                    padding: '20px', 
                    borderRadius: '8px', 
                    border: `2px solid ${wizardState.isFinalReviewConfirmed ? '#22c55e' : 'var(--color-border)'}`,
                    transition: 'all 0.2s',
                    marginTop: '8px'
                }}
            >
                <label style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', cursor: 'pointer' }}>
                    <div style={{ marginTop: '2px' }}>
                        <input 
                            type="checkbox" 
                            checked={wizardState.isFinalReviewConfirmed}
                            onChange={(e) => wizardState.setIsFinalReviewConfirmed(e.target.checked)}
                            style={{ 
                                width: '18px', 
                                height: '18px', 
                                accentColor: 'var(--color-primary)',
                                cursor: 'pointer'
                            }} 
                        />
                    </div>
                    <div>
                        <span style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', marginBottom: '4px' }}>
                            Confirmação de Revisão
                        </span>
                        <span style={{ display: 'block', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                            Confirmo que revisei os dados do fornecedor, os itens da cotação e o resumo financeiro antes de salvar esta cotação.
                        </span>
                    </div>
                </label>
            </div>
        </div>
    );
};
