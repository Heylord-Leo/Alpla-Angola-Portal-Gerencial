import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Layers, CheckCircle } from 'lucide-react';
import { itEquipmentCatalogApi, itEquipmentApi } from '../../../lib/itEquipmentApi';
import { WizardLayout } from '../../../components/common/wizard/WizardLayout';
import type { WizardStep } from '../../../components/common/wizard/WizardStepIndicator';
import { SectionCard } from '../../../components/common/ui/SectionCard';
import { FormInput } from '../../../components/common/form/FormInput';
import { FormSearchableSelect } from '../../../components/common/form/FormSearchableSelect';
import { useUnsavedChangesWarning } from '../../../hooks/useUnsavedChangesWarning';
import { ConfirmationDialog } from '../../../components/common/ConfirmationDialog';

const STEPS: WizardStep[] = [
    { key: 'basic', label: 'Informações Básicas', description: 'Nome, Fabricante, Tipo' },
    { key: 'status', label: 'Estado e Ordenação', description: 'Ativo/Inativo, Ordem' },
    { key: 'review', label: 'Revisão', description: 'Confirmar e Guardar' },
];

export default function ModelWizardPage() {
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

    // Lookups
    const [manufacturers, setManufacturers] = useState<Array<{ value: string; label: string }>>([]);
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);
    const [loadingData, setLoadingData] = useState(isEdit);

    // Form State
    const [form, setForm] = useState({
        name: '',
        manufacturerId: '',
        equipmentTypeCode: '',
        sortOrder: '0',
        isActive: true,
    });

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
                const [mfrs, types] = await Promise.all([
                    itEquipmentCatalogApi.manufacturers.list(true),
                    itEquipmentApi.types.list(true)
                ]);
                
                setManufacturers(mfrs.map((m: any) => ({ value: m.id, label: m.name })));
                setEquipmentTypes(types.map((t: any) => ({ value: t.code, label: t.displayName })));

                if (isEdit) {
                    // Fetch model detail. Our mock list API returns all models, we can find it.
                    const allModels = await itEquipmentCatalogApi.models.list();
                    const model = allModels.find((m: any) => m.id === id);
                    if (model) {
                        setForm({
                            name: model.name || '',
                            manufacturerId: model.manufacturerId || '',
                            equipmentTypeCode: model.equipmentTypeCode || '',
                            sortOrder: (model.sortOrder || 0).toString(),
                            isActive: model.isActive !== false,
                        });
                    } else {
                        setGlobalError('Modelo não encontrado.');
                    }
                }
            } catch (err: any) {
                setGlobalError('Falha ao carregar dados auxiliares.');
            } finally {
                setLoadingData(false);
            }
        };
        load();
    }, [id, isEdit]);

    const validateStep = (stepIdx: number): boolean => {
        if (stepIdx === 0) {
            if (!form.name.trim()) {
                setStepErrors({ basic: 'O nome do modelo é obrigatório.' });
                return false;
            }
            if (!form.manufacturerId) {
                setStepErrors({ basic: 'O fabricante é obrigatório.' });
                return false;
            }
            if (!form.equipmentTypeCode) {
                setStepErrors({ basic: 'O tipo de equipamento é obrigatório.' });
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

            const payload: any = {
                name: form.name.trim(),
                equipmentTypeCode: form.equipmentTypeCode,
                sortOrder: parseInt(form.sortOrder) || 0,
            };

            if (isEdit) {
                payload.isActive = form.isActive;
                await itEquipmentCatalogApi.models.update(id as string, payload);
            } else {
                payload.manufacturerId = form.manufacturerId;
                await itEquipmentCatalogApi.models.create(payload);
            }

            setIsSubmitted(true);
            setIsDirty(false);
            
            // Navigate back to catalogs page
            navigate('/it/catalogs');
        } catch (err: any) {
            setGlobalError(err.message || 'Falha ao guardar o modelo.');
        } finally {
            setSaving(false);
        }
    };

    const handleCancel = () => {
        if (isDirty) {
            confirmNavigation(() => navigate('/it/catalogs'));
        } else {
            navigate('/it/catalogs');
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
                        { label: 'Catálogos', to: '/it/catalogs' },
                        { label: isEdit ? 'Editar Modelo' : 'Novo Modelo' },
                    ]}
                    title={isEdit ? 'Editar Modelo' : 'Novo Modelo'}
                    subtitle="Preencha os detalhes do modelo de equipamento."
                    titleIcon={<Layers size={28} />}
                    steps={STEPS}
                    currentStep={currentStep}
                    completedSteps={completedSteps}
                    onBack={currentStep === 0 ? handleCancel : handleBack}
                    onNext={currentStep < STEPS.length - 1 ? handleNext : handleSave}
                    isSubmitting={saving}
                    canProceed={true}
                    submitLabel={isEdit ? 'Atualizar Modelo' : 'Criar Modelo'}
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
                                            label="Nome do Modelo *"
                                            value={form.name}
                                            onChange={v => set('name', v)}
                                            placeholder="Ex: Latitude 5420"
                                        />
                                    </div>
                                    <FormSearchableSelect
                                        label="Fabricante *"
                                        value={form.manufacturerId}
                                        onChange={v => set('manufacturerId', v)}
                                        options={manufacturers}
                                        placeholder="Selecione o fabricante"
                                        disabled={!!isEdit} // Block changing manufacturer on edit if backend doesn't support it
                                    />
                                    <FormSearchableSelect
                                        label="Tipo de Equipamento *"
                                        value={form.equipmentTypeCode}
                                        onChange={v => set('equipmentTypeCode', v)}
                                        options={equipmentTypes}
                                        placeholder="Selecione o tipo"
                                    />
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
                                            id="model-active"
                                            checked={form.isActive}
                                            onChange={(e) => set('isActive', e.target.checked)}
                                            style={{ width: 16, height: 16, cursor: 'pointer' }}
                                        />
                                        <label htmlFor="model-active" style={{ fontSize: '0.9rem', cursor: 'pointer', fontWeight: 500 }}>
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
                                        Reveja as informações abaixo. Clique em "Guardar" para {isEdit ? 'atualizar' : 'criar'} este modelo.
                                    </p>
                                </div>
                            </div>
                            
                            <SectionCard title="Resumo do Modelo">
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                    <ReviewItem label="Nome do Modelo" value={form.name} />
                                    <ReviewItem label="Fabricante" value={manufacturers.find(m => m.value === form.manufacturerId)?.label || form.manufacturerId} />
                                    <ReviewItem label="Tipo de Equipamento" value={equipmentTypes.find(t => t.value === form.equipmentTypeCode)?.label || form.equipmentTypeCode} />
                                    <ReviewItem label="Ordem" value={form.sortOrder} />
                                    <ReviewItem label="Estado" value={form.isActive ? 'Ativo' : 'Inativo'} />
                                </div>
                            </SectionCard>
                        </div>
                    )}
                </WizardLayout>
            )}

            {/* Leave Confirmation Dialog */}
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
