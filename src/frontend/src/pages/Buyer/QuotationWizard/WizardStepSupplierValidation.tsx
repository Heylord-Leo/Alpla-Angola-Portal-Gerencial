import React, { useState, useEffect } from 'react';
import { OcrDraft } from '../../../types';
import { Building2, AlertTriangle, AlertCircle, PlusCircle, CheckCircle2 } from 'lucide-react';
import { UseQuotationWizardStateReturn } from './hooks/useQuotationWizardState';
import { QuickSupplierModal } from '../../../components/Buyer/QuickSupplierModal';
import { SupplierAutocomplete } from '../../../components/SupplierAutocomplete';
import { SupplierAdditionalInfoPanel, SupplierAdditionalInfo } from '../../../components/suppliers/SupplierAdditionalInfoPanel';

interface WizardStepSupplierValidationProps {
    draft: OcrDraft | null;
    wizardState: UseQuotationWizardStateReturn;
}

export const WizardStepSupplierValidation: React.FC<WizardStepSupplierValidationProps> = ({ draft, wizardState }) => {
    const [isSupplierModalOpen, setIsSupplierModalOpen] = useState(false);
    
    // Optional Supplier Enrichment State
    const [isOptionalPanelExpanded, setIsOptionalPanelExpanded] = useState(false);
    const [optionalData, setOptionalData] = useState<SupplierAdditionalInfo>({
        Name: '',
        TaxId: '',
        PrimaveraCode: '',
        Address: '',
        ContactName1: '',
        ContactEmail1: '',
        ContactPhone1: '',
        BankIban: '',
        BankAccountNumber: '',
        BankSwift: '',
        PaymentTerms: ''
    });

    useEffect(() => {
        if (draft) {
            setOptionalData({
                Name: draft.supplierNameSnapshot || '',
                TaxId: draft.supplierTaxId || '',
                PrimaveraCode: draft.supplierPrimaveraCode || '',
                Address: draft.supplierAddress || '',
                ContactName1: draft.supplierContactName || '',
                ContactEmail1: draft.supplierEmail || '',
                ContactPhone1: draft.supplierPhone || '',
                BankIban: draft.supplierBankIban || '',
                BankAccountNumber: draft.supplierBankAccountNumber || '',
                BankSwift: draft.supplierBankSwift || '',
                PaymentTerms: draft.supplierPaymentTerms || ''
            });
        }
    }, [draft?.supplierNameSnapshot, draft?.supplierTaxId, draft?.supplierPrimaveraCode, draft?.supplierAddress, draft?.supplierContactName, draft?.supplierEmail, draft?.supplierPhone, draft?.supplierBankIban, draft?.supplierBankAccountNumber, draft?.supplierBankSwift, draft?.supplierPaymentTerms]);

    if (!draft) return null;

    const isSupplierDraft = draft.supplierId && draft.supplierRegistrationStatus === 'DRAFT';
    const isSupplierPendingValidation = draft.supplierId && draft.supplierRegistrationStatus !== 'DRAFT' && !draft.supplierPrimaveraCode;
    const isSupplierValid = draft.supplierId && draft.supplierRegistrationStatus === 'ACTIVE' && !!draft.supplierPrimaveraCode;
    const hasSupplier = !!draft.supplierId;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', paddingBottom: '32px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <Building2 size={18} /> Validação do Fornecedor
                </h3>
                <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Confirme ou selecione o fornecedor desta cotação. O cadastro deve estar completo para permitir a emissão de ordens de compra futuras.
                </p>
                
                <div style={{ backgroundColor: isSupplierValid ? 'var(--color-status-success-light, #f0fdf4)' : (isSupplierDraft || isSupplierPendingValidation) ? 'var(--color-status-warning-light, #fffbeb)' : '#fff', border: `1px solid ${isSupplierValid ? 'var(--color-status-success-border, #bbf7d0)' : (isSupplierDraft || isSupplierPendingValidation) ? 'var(--color-status-warning-border, #fcd34d)' : 'var(--color-border)'}`, borderRadius: '8px', padding: '16px' }}>
                    {hasSupplier ? (
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                            <div>
                                <h4 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: '0 0 4px 0' }}>
                                    {draft.supplierNameSnapshot}
                                </h4>
                                <div style={{ display: 'flex', gap: '12px', fontSize: '0.875rem', color: 'var(--color-text-muted)', marginBottom: '12px' }}>
                                    <span>NIF: {draft.supplierTaxId || 'N/A'}</span>
                                    <span>Portal: {draft.supplierPortalCode || 'N/A'}</span>
                                    {!isSupplierDraft && <span>Primavera: {draft.supplierPrimaveraCode || 'N/A'}</span>}
                                </div>
                                
                                {isSupplierDraft && (
                                    <div style={{ marginTop: '4px' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                            <AlertTriangle size={18} color="var(--color-status-warning-text, #b45309)" />
                                            <span style={{ display: 'inline-block', backgroundColor: 'var(--color-status-warning-light)', color: 'var(--color-status-warning-text, #b45309)', border: '1px solid var(--color-status-warning-border, #f59e0b)', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>
                                                Cadastro de fornecedor necessário
                                            </span>
                                        </div>
                                        <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                                            Este fornecedor foi criado como rascunho e precisa ser completado/validado em Contratos → Fichas de Fornecedor antes da emissão da P.O.
                                        </p>
                                        
                                        {/* Optional enrichment — now the shared component, so the
                                            payment editor offers exactly these fields, labels and
                                            persistence rather than a second, divergent copy. */}
                                        <SupplierAdditionalInfoPanel
                                            supplierId={draft.supplierId!}
                                            initial={optionalData}
                                            defaultExpanded={isOptionalPanelExpanded}
                                        />
                                    </div>
                                )}
                                
                                {isSupplierPendingValidation && (
                                    <div style={{ marginTop: '4px' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                            <AlertTriangle size={18} color="var(--color-status-warning-text, #b45309)" />
                                            <span style={{ display: 'inline-block', backgroundColor: 'var(--color-status-warning-light)', color: 'var(--color-status-warning-text, #b45309)', border: '1px solid var(--color-status-warning-border, #f59e0b)', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>
                                                Fornecedor pendente de validação
                                            </span>
                                        </div>
                                        <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                                            Este fornecedor existe no portal, mas ainda precisa ser completado/validado antes da emissão da P.O.
                                        </p>
                                    </div>
                                )}
                                
                                {isSupplierValid && (
                                    <div style={{ marginTop: '4px' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                            <CheckCircle2 size={18} color="var(--color-status-success-text, #15803d)" />
                                            <span style={{ display: 'inline-block', backgroundColor: 'var(--color-status-success-light, #f0fdf4)', color: 'var(--color-status-success-text, #15803d)', border: '1px solid var(--color-status-success-border, #bbf7d0)', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>
                                                Fornecedor cadastrado
                                            </span>
                                        </div>
                                        <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                                            Fornecedor validado. Nenhuma ação necessária.
                                        </p>
                                    </div>
                                )}
                            </div>
                            <button
                                onClick={() => wizardState.updateDraftHeader('supplierId', null)}
                                style={{ background: 'none', border: 'none', color: 'var(--color-primary)', fontSize: '0.875rem', fontWeight: 500, cursor: 'pointer', textDecoration: 'underline' }}
                            >
                                Alterar Fornecedor
                            </button>
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-status-error-text, #b91c1c)', backgroundColor: 'var(--color-status-error-light, #fef2f2)', border: '1px solid var(--color-status-error-border, #fecaca)', padding: '12px', borderRadius: '6px' }}>
                                <AlertCircle size={20} />
                                <span style={{ fontSize: '0.875rem', fontWeight: 500 }}>Fornecedor não identificado. Selecione um fornecedor existente ou cadastre um novo para prosseguir.</span>
                            </div>
                            
                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-end' }}>
                                <div style={{ flex: 1 }}>
                                    <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', marginBottom: '4px', textTransform: 'uppercase' }}>
                                        Buscar Fornecedor Existente
                                    </label>
                                    <SupplierAutocomplete
                                        initialName={draft.supplierNameSnapshot}
                                        onChange={(id, name, portalCode, primaveraCode, registrationStatus) => {
                                            wizardState.updateDraftHeader('supplierId', id);
                                            wizardState.updateDraftHeader('supplierNameSnapshot', name);
                                            wizardState.updateDraftHeader('supplierPortalCode', portalCode);
                                            wizardState.updateDraftHeader('supplierPrimaveraCode', primaveraCode);
                                            wizardState.updateDraftHeader('supplierRegistrationStatus', registrationStatus);
                                        }}
                                    />
                                </div>
                                <div style={{ color: 'var(--color-text-muted)', fontSize: '0.875rem', fontWeight: 500, paddingBottom: '8px' }}>ou</div>
                                <button
                                    onClick={() => setIsSupplierModalOpen(true)}
                                    style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', backgroundColor: '#fff', border: '1px solid var(--color-primary)', color: 'var(--color-primary)', fontWeight: 600, cursor: 'pointer', height: '38px' }}
                                >
                                    <PlusCircle size={16} /> Cadastrar Novo
                                </button>
                            </div>
                        </div>
                    )}
                </div>


            </div>

            {/* Modals */}
            <QuickSupplierModal
                isOpen={isSupplierModalOpen}
                onClose={() => setIsSupplierModalOpen(false)}
                initialName={draft.supplierNameSnapshot}
                initialTaxId={draft.supplierTaxId || ''}
                onSuccess={(supplier: { id: number; name: string; portalCode?: string }) => {
                    wizardState.updateDraftHeader('supplierId', supplier.id);
                    wizardState.updateDraftHeader('supplierNameSnapshot', supplier.name);
                    wizardState.updateDraftHeader('supplierPortalCode', supplier.portalCode);
                    wizardState.updateDraftHeader('supplierPrimaveraCode', undefined);
                    wizardState.updateDraftHeader('supplierRegistrationStatus', 'DRAFT');
                    
                    // Auto-expand optional section after creation
                    setIsOptionalPanelExpanded(true);
                    
                    setIsSupplierModalOpen(false);
                }}
            />
        </div>
    );
};
