import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Layers, CheckCircle } from 'lucide-react';
import { itEquipmentApi } from '../../../lib/itEquipmentApi';
import { WizardLayout } from '../../../components/common/wizard/WizardLayout';
import type { WizardStep } from '../../../components/common/wizard/WizardStepIndicator';
import { SectionCard } from '../../../components/common/ui/SectionCard';
import { FormInput } from '../../../components/common/form/FormInput';
import { useUnsavedChangesWarning } from '../../../hooks/useUnsavedChangesWarning';
import { ConfirmationDialog } from '../../../components/common/ConfirmationDialog';

const STEPS: WizardStep[] = [
    { key: 'basic', label: 'Informações Básicas', description: 'Nome e Código' },
    { key: 'status', label: 'Regras e Estado', description: 'Ativo/Inativo, Ordem' },
    { key: 'review', label: 'Revisão', description: 'Confirmar e Guardar' },
];

export default function TypeWizardPage() {
    const navigate = useNavigate();
    const { id } = useParams<{ id: string }>();
    const isEdit = id && id !== 'new';

    const [currentStep, setCurrentStep] = useState(0);
    const [completedSteps, setCompletedSteps] = useState<Set<number>>(new Set());
    const [stepErrors, setStepErrors] = useState<Record<string, string>>({});
    const [globalError, setGlobalError] = useState('');
    const [saving, setSaving] = useState(false);
    const [isDirty, setIsDirty] = useState(false);
    const [isSubmitted, setIsSubmitted] = useState(false);

    const [loadingData, setLoadingData] = useState(!!isEdit);

    // Form State
    const [form, setForm] = useState({
        displayName: '',
        code: '',
        sortOrder: '0',
        isActive: true,
    });

    // To check for duplicate codes locally if creating
    const [existingCodes, setExistingCodes] = useState<string[]>([]);

    const set = (field: string, value: any) => {
        setForm(prev => ({ ...prev, [field]: value }));
        setIsDirty(true);
    };

    // Unsaved changes
    const {
        showLeaveDialog,
        confirmNavigation,
        handleConfirmLeave,
        handleCancelLeave,
    } = useUnsavedChangesWarning({ isDirty, isSubmitted });

    useEffect(() => {
        const load = async () => {
            try {
                const types = await itEquipmentApi.types.list(false);
                setExistingCodes(types.map((t: any) => t.code));

                if (isEdit) {
                    const type = types.find((t: any) => t.id === id);
                    if (type) {
                        setForm({
                            displayName: type.displayName || '',
                            code: type.code || '',
                            sortOrder: (type.sortOrder || 0).toString(),
                            isActive: type.isActive !== false,
                        });
                    } else {
                        setGlobalError('Tipo não encontrado.');
                    }
                }
            } catch (err: any) {
                setGlobalError('Falha ao carregar tipos existentes.');
            } finally {
                setLoadingData(false);
            }
        };
        load();
    }, [id, isEdit]);

    const validateStep = (stepIdx: number): boolean => {
        if (stepIdx === 0) {
            if (!form.displayName.trim()) {
                setStepErrors({ basic: 'O nome do tipo é obrigatório.' });
                return false;
            }
            if (!form.code.trim()) {
                setStepErrors({ basic: 'O código é obrigatório.' });
                return false;
            }
            
            const codeUpper = form.code.trim().toUpperCase().replace(/\s+/g, '_').replace(/[^A-Z0-9_]/g, '');
            if (codeUpper.length < 2) {
                setStepErrors({ basic: 'O código deve ter pelo menos 2 caracteres alfanuméricos.' });
                return false;
            }

            if (!isEdit && existingCodes.includes(codeUpper)) {
                setStepErrors({ basic: `Já existe um tipo com o código "${codeUpper}".` });
                return false;
            }
        }
        if (stepIdx === 1) {
            const parsedSort = parseInt(form.sortOrder);
            if (isNaN(parsedSort) || parsedSort < 0) {
                setStepErrors({ status: 'A ordem de exibição deve ser um número válido.' });
                return false;
            }
        }
        
        setStepErrors(prev => {
            const next = { ...prev };
            delete next[STEPS[stepIdx].key];
            return next;
        });
        return true;
    };

    const handleNext = () => {
        // Auto format code before validating
        if (currentStep === 0 && !isEdit) {
            const formattedCode = form.code.trim().toUpperCase().replace(/\s+/g, '_').replace(/[^A-Z0-9_]/g, '');
            set('code', formattedCode);
        }

        if (validateStep(currentStep)) {
            setCompletedSteps(prev => new Set(prev).add(currentStep));
            setCurrentStep(c => c + 1);
        }
    };

    const handleBack = () => setCurrentStep(c => Math.max(0, c - 1));

    const handleSave = async () => {
        if (!validateStep(0) || !validateStep(1)) return;

        try {
            setSaving(true);
            setGlobalError('');

            const formattedCode = form.code.trim().toUpperCase().replace(/\s+/g, '_').replace(/[^A-Z0-9_]/g, '');

            const payload: any = {
                displayName: form.displayName.trim(),
                sortOrder: parseInt(form.sortOrder) || 0,
                isActive: form.isActive,
            };

            if (isEdit) {
                await itEquipmentApi.types.update(id as string, payload);
            } else {
                payload.code = formattedCode;
                await itEquipmentApi.types.create(payload);
            }

            setIsSubmitted(true);
            setIsDirty(false);
            
            navigate('/it/types');
        } catch (err: any) {
            setGlobalError(err.message || 'Falha ao guardar o tipo.');
        } finally {
            setSaving(false);
        }
    };

    const handleCancel = () => {
        if (isDirty) {
            confirmNavigation(() => navigate('/it/types'));
        } else {
            navigate('/it/types');
        }
    };

    return (
        <>
            {loadingData ? (
                <div style={{ display: 'flex', justifyContent: 'center', padding: '100px 0', color: 'var(--color-text-muted)' }}>
                    A carregar dados...
                </div>
            ) : (
                <WizardLayout
                    breadcrumbs={[
                        { label: 'T.I.', to: '/it/equipment' },
                        { label: 'Tipos de Equipamento', to: '/it/types' },
                        { label: isEdit ? 'Editar Tipo' : 'Novo Tipo' },
                    ]}
                    title={isEdit ? 'Editar Tipo de Equipamento' : 'Novo Tipo de Equipamento'}
                    subtitle="Defina as informações deste tipo de equipamento."
                    titleIcon={<Layers size={28} />}
                    steps={STEPS}
                    currentStep={currentStep}
                    completedSteps={completedSteps}
                    onBack={currentStep === 0 ? handleCancel : handleBack}
                    onNext={currentStep < STEPS.length - 1 ? handleNext : handleSave}
                    isSubmitting={saving}
                    submitLabel={isEdit ? 'Atualizar Tipo' : 'Criar Tipo'}
                >
                    {globalError && (
                        <div style={{ padding: '12px 16px', backgroundColor: '#fee2e2', color: '#b91c1c', borderRadius: '8px', marginBottom: '16px', fontSize: '0.9rem' }}>
                            {globalError}
                        </div>
                    )}
                    {stepErrors[STEPS[currentStep].key] && (
                        <div style={{ padding: '12px 16px', backgroundColor: '#fee2e2', color: '#b91c1c', borderRadius: '8px', marginBottom: '16px', fontSize: '0.9rem' }}>
                            {stepErrors[STEPS[currentStep].key]}
                        </div>
                    )}

                    {currentStep === 0 && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            <SectionCard title="Informações Básicas">
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                    <div style={{ gridColumn: '1 / -1' }}>
                                        <FormInput
                                            label="Nome de Exibição *"
                                            value={form.displayName}
                                            onChange={v => set('displayName', v)}
                                            placeholder="Ex: Laptop"
                                        />
                                    </div>
                                    <div style={{ gridColumn: '1 / -1' }}>
                                        <FormInput
                                            label="Código do Tipo *"
                                            value={form.code}
                                            onChange={v => {
                                                // auto format to uppercase without spaces as they type, but let them type.
                                                set('code', v.toUpperCase().replace(/\s+/g, '_'));
                                            }}
                                            placeholder="Ex: LAPTOP"
                                            disabled={!!isEdit} // Immutability on edit as per rules
                                        />
                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 4 }}>
                                            Usado internamente e em integrações. Apenas letras, números e sublinhados. {isEdit && "Não pode ser alterado."}
                                        </div>
                                    </div>
                                </div>
                            </SectionCard>
                        </div>
                    )}

                    {currentStep === 1 && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            <SectionCard title="Estado e Ordenação">
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '16px' }}>
                                    <FormInput
                                        label="Ordem de Exibição"
                                        type="number"
                                        value={form.sortOrder}
                                        onChange={v => set('sortOrder', v)}
                                        placeholder="0"
                                    />
                                    
                                    <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 8 }}>
                                        <input 
                                            type="checkbox" 
                                            id="type-active"
                                            checked={form.isActive}
                                            onChange={(e) => set('isActive', e.target.checked)}
                                            style={{ width: 16, height: 16, cursor: 'pointer' }}
                                        />
                                        <label htmlFor="type-active" style={{ fontSize: '0.9rem', cursor: 'pointer', fontWeight: 500 }}>
                                            Registo Ativo (permitir seleção em novos cadastros)
                                        </label>
                                    </div>
                                </div>
                            </SectionCard>
                        </div>
                    )}

                    {currentStep === 2 && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            <div style={{
                                backgroundColor: '#f0f9ff', border: '1px solid #bae6fd', borderRadius: '8px',
                                padding: '20px', display: 'flex', alignItems: 'flex-start', gap: '16px'
                            }}>
                                <CheckCircle style={{ color: '#0284c7', marginTop: '2px', flexShrink: 0 }} />
                                <div>
                                    <h3 style={{ margin: '0 0 4px', color: '#0369a1', fontSize: '1.05rem', fontWeight: 600 }}>Pronto para guardar</h3>
                                    <p style={{ margin: 0, color: '#0c4a6e', fontSize: '0.9rem', lineHeight: 1.5 }}>
                                        Reveja as informações abaixo. Clique em "Guardar" para {isEdit ? 'atualizar' : 'criar'} este tipo de equipamento.
                                    </p>
                                </div>
                            </div>
                            
                            <SectionCard title="Resumo do Tipo">
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                    <ReviewItem label="Nome de Exibição" value={form.displayName} />
                                    <ReviewItem label="Código" value={form.code} />
                                    <ReviewItem label="Ordem" value={form.sortOrder} />
                                    <ReviewItem label="Estado" value={form.isActive ? 'Ativo' : 'Inativo'} />
                                </div>
                            </SectionCard>
                        </div>
                    )}
                </WizardLayout>
            )}

            {showLeaveDialog && (
                <ConfirmationDialog
                    title="Descartar alterações?"
                    message="Tem a certeza que deseja sair? As informações não guardadas serão perdidas."
                    confirmText="Sim, descartar e sair"
                    cancelText="Cancelar"
                    variant="destructive"
                    onConfirm={handleConfirmLeave}
                    onCancel={handleCancelLeave}
                />
            )}
        </>
    );
}

function ReviewItem({ label, value }: { label: string; value: React.ReactNode }) {
    return (
        <div>
            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', fontWeight: 600, marginBottom: '4px' }}>
                {label}
            </div>
            <div style={{ fontSize: '0.95rem', color: 'var(--color-text-main)', fontWeight: 500 }}>
                {value || '-'}
            </div>
        </div>
    );
}
