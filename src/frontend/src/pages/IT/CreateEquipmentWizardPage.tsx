import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Monitor, CheckCircle, Printer, ArrowRight, ExternalLink, Plus } from 'lucide-react';
import { itEquipmentApi, itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { PurchaseRequestPicker } from '../../components/common/form/PurchaseRequestPicker';
import { api } from '../../lib/api';
import type { ITEquipmentTypeItem, MasterDataCompany, MasterDataPlant, CatalogManufacturer, CatalogModel, CatalogProcessor, CatalogMemoryOption } from '../../types/itEquipment';
import { WizardLayout } from '../../components/common/wizard/WizardLayout';
import type { WizardStep } from '../../components/common/wizard/WizardStepIndicator';
import { SectionCard } from '../../components/common/ui/SectionCard';
import { FormInput } from '../../components/common/form/FormInput';
import { FormSelect } from '../../components/common/form/FormSelect';
import { FormSearchableSelect } from '../../components/common/form/FormSearchableSelect';
import { FormTextarea } from '../../components/common/form/FormTextarea';
import { FormCheckbox } from '../../components/common/form/FormCheckbox';
import { FileUpload } from '../../components/common/form/FileUpload';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { useUnsavedChangesWarning } from '../../hooks/useUnsavedChangesWarning';

// ─── Step definitions ───
const STEPS: WizardStep[] = [
    { key: 'basic', label: 'Identificação Básica', description: 'Origem, tipo, empresa, planta' },
    { key: 'technical', label: 'Especificações Técnicas', description: 'Fabricante, modelo, hardware' },
    { key: 'purchase', label: 'Compra / Rastreabilidade', description: 'Fornecedor, valor, documento' },
    { key: 'warranty', label: 'Garantia', description: 'Duração, datas, notas' },
    { key: 'review', label: 'Revisão e Criação', description: 'Resumo final' },
];

const BREADCRUMBS = [
    { label: 'T.I.', to: '/it/equipment' },
    { label: 'Estoque de Equipamentos', to: '/it/equipment' },
    { label: 'Novo Equipamento' },
];

// ─── Type-sensitive field visibility (same logic as EquipmentFormModal) ───
const COMPUTE_TYPES = ['LAPTOP', 'DESKTOP', 'SERVER', 'TABLET'];
const NETWORK_TYPES = ['SWITCH', 'FIREWALL', 'ACCESS_POINT', 'NETWORK_EQUIPMENT', 'NVR'];

function buildOptionsWithLegacy(
    options: Array<{ value: string; label: string }>,
    currentValue: string,
    placeholder: string
): Array<{ value: string; label: string }> {
    const opts: Array<{ value: string; label: string }> = [{ value: '', label: placeholder }];
    if (currentValue && !options.some(o => o.value === currentValue)) {
        opts.push({ value: currentValue, label: `${currentValue} (valor existente)` });
    }
    opts.push(...options);
    return opts;
}


export default function CreateEquipmentWizardPage({ onExit, onModeChange }: { onExit?: () => void, onModeChange?: () => void } = {}) {
    const navigate = useNavigate();
    const [currentStep, setCurrentStep] = useState(0);
    const [completedSteps, setCompletedSteps] = useState<Set<number>>(new Set());
    const [stepErrors, setStepErrors] = useState<Record<string, string>>({});
    const [globalError, setGlobalError] = useState('');
    const [saving, setSaving] = useState(false);
    const [isDirty, setIsDirty] = useState(false);

    // ─── Post-creation success state ───
    const [creationResult, setCreationResult] = useState<{
        id: string; assetTag: string; assetCode: string; qrCodeUrl: string;
    } | null>(null);
    const [purchaseDocWarning, setPurchaseDocWarning] = useState('');

    // ─── Master data lookups ───
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);
    const [companies, setCompanies] = useState<MasterDataCompany[]>([]);
    const [plants, setPlants] = useState<MasterDataPlant[]>([]);
    const [manufacturers, setManufacturers] = useState<CatalogManufacturer[]>([]);
    const [models, setModels] = useState<CatalogModel[]>([]);
    const [processors, setProcessors] = useState<CatalogProcessor[]>([]);
    const [memoryOptions, setMemoryOptions] = useState<CatalogMemoryOption[]>([]);

    // ─── Form state (identical structure to EquipmentFormModal) ───
    const [form, setForm] = useState({
        hostname: '',
        companyId: '',
        plantId: '',
        plant: '',
        equipmentType: 'LAPTOP',
        statusCode: 'AVAILABLE',
        manufacturer: '',
        model: '',
        serialNumber: '',
        macAddress: '',
        wifiMacAddress: '',
        processor: '',
        memoryRam: '',
        color: '',
        biometricMfaEnabled: false,
        idCard: '',
        notes: '',
        sourceType: 'MANUAL_REGISTRATION',
        legacyAssetCode: '',
        manufactureDate: '',
    });

    const [purchase, setPurchase] = useState({
        purchaseAmount: '',
        currency: 'AOA',
        acquisitionDate: '',
        supplierId: null as number | null,
        supplierName: '',
        supplierPortalCode: '',
        purchaseRequestId: '',
        purchaseRequestNumber: '',
        purchaseOrderNumber: '',
        invoiceNumber: '',
        purchaseInfoUnavailable: false,
        purchaseInfoUnavailableReason: '',
    });

    const [warranty, setWarranty] = useState({
        warrantyMonths: '',
        warrantyStartDate: '',
        warrantyEndDate: '',
        warrantyNotes: '',
        warrantyInfoUnavailable: false,
        warrantyInfoUnavailableReason: '',
    });

    const [purchaseDocFile, setPurchaseDocFile] = useState<File | null>(null);

    const set = (field: string, value: any) => { setForm(prev => ({ ...prev, [field]: value })); setIsDirty(true); };
    const setPur = (field: string, value: any) => { setPurchase(prev => ({ ...prev, [field]: value })); setIsDirty(true); };
    const setWar = (field: string, value: any) => { setWarranty(prev => ({ ...prev, [field]: value })); setIsDirty(true); };

    // ─── Unsaved data navigation protection (BrowserRouter-compatible) ───
    const {
        showLeaveDialog,
        confirmNavigation,
        handleConfirmLeave,
        handleCancelLeave,
    } = useUnsavedChangesWarning({ isDirty, isSubmitted: !!creationResult });

    // ─── Load master data on mount ───
    useEffect(() => {
        itEquipmentApi.types.list(true).then(types => {
            setEquipmentTypes(types.map((t: ITEquipmentTypeItem) => ({ value: t.code, label: t.displayName })));
        }).catch(() => {
            setEquipmentTypes([
                { value: 'LAPTOP', label: 'Laptop' },
                { value: 'DESKTOP', label: 'Desktop' },
                { value: 'MONITOR', label: 'Monitor' },
                { value: 'UNKNOWN', label: 'Desconhecido' },
            ]);
        });
        api.lookups.getCompanies().then(setCompanies).catch(() => {});
        itEquipmentCatalogApi.manufacturers.list(true).then(setManufacturers).catch(() => {});
        itEquipmentCatalogApi.processors.list(true).then(setProcessors).catch(() => {});
        itEquipmentCatalogApi.memoryOptions.list(true).then(setMemoryOptions).catch(() => {});
    }, []);

    // ─── Company → Plant cascade ───
    useEffect(() => {
        if (form.companyId) {
            api.lookups.getPlants(Number(form.companyId)).then(setPlants).catch(() => setPlants([]));
        } else {
            setPlants([]);
        }
    }, [form.companyId]);

    // ─── Manufacturer → Model cascade ───
    useEffect(() => {
        if (form.manufacturer) {
            const mfr = manufacturers.find(m => m.name === form.manufacturer);
            if (mfr) {
                itEquipmentCatalogApi.models.list({
                    activeOnly: true,
                    manufacturerId: mfr.id,
                    equipmentTypeCode: form.equipmentType || undefined
                }).then(setModels).catch(() => setModels([]));
            } else {
                setModels([]);
            }
        } else {
            setModels([]);
        }
    }, [form.manufacturer, form.equipmentType, manufacturers]);

    // ─── Auto-calculate warranty end date ───
    useEffect(() => {
        const months = parseInt(warranty.warrantyMonths);
        if (!months || months <= 0) return;
        const startDate = warranty.warrantyStartDate || purchase.acquisitionDate;
        if (!startDate) return;
        const start = new Date(startDate);
        if (isNaN(start.getTime())) return;
        const end = new Date(start);
        end.setMonth(end.getMonth() + months);
        const endStr = end.toISOString().split('T')[0];
        if (endStr !== warranty.warrantyEndDate) {
            setWarranty(prev => ({ ...prev, warrantyEndDate: endStr }));
        }
    }, [warranty.warrantyMonths, warranty.warrantyStartDate, purchase.acquisitionDate]);

    // ─── Auto-populate warranty start from acquisition date ───
    useEffect(() => {
        if (!warranty.warrantyStartDate && purchase.acquisitionDate) {
            setWarranty(prev => ({ ...prev, warrantyStartDate: purchase.acquisitionDate }));
        }
    }, [purchase.acquisitionDate]);

    // ─── Type-sensitive visibility ───
    const showHostname = [...COMPUTE_TYPES, ...NETWORK_TYPES].includes(form.equipmentType);
    const showProcessorRam = COMPUTE_TYPES.includes(form.equipmentType);
    const showMacAddress = [...COMPUTE_TYPES, ...NETWORK_TYPES, 'PRINTER'].includes(form.equipmentType);

    // ─── Build options ───
    const manufacturerOpts = useMemo(() => buildOptionsWithLegacy(
        manufacturers.map(m => ({ value: m.name, label: m.name })), form.manufacturer, 'Selecione...'
    ), [manufacturers, form.manufacturer]);

    const modelOpts = useMemo(() => buildOptionsWithLegacy(
        models.map(m => ({ value: m.name, label: m.name })), form.model,
        form.manufacturer ? 'Selecione...' : 'Selecione fabricante primeiro'
    ), [models, form.model, form.manufacturer]);

    const processorOpts = useMemo(() => buildOptionsWithLegacy(
        processors.map(p => ({ value: p.name, label: p.name })), form.processor, 'Selecione...'
    ), [processors, form.processor]);

    const memoryOpts = useMemo(() => buildOptionsWithLegacy(
        memoryOptions.map(m => ({ value: m.displayName, label: m.displayName })), form.memoryRam, 'Selecione...'
    ), [memoryOptions, form.memoryRam]);

    // ─── Per-step validation (same rules as EquipmentFormModal) ───
    const validateStep = useCallback((step: number): boolean => {
        const errors: Record<string, string> = {};

        if (step === 0) {
            if (!form.companyId) errors.companyId = 'Obrigatório.';
            if (!form.plantId) errors.plantId = 'Obrigatório.';
            if (!form.equipmentType) errors.equipmentType = 'Obrigatório.';
        }

        if (step === 2) {
            if (!purchase.purchaseInfoUnavailable) {
                if (!purchase.purchaseAmount.toString().trim()) errors.purchaseAmount = 'Obrigatório.';
                if (!purchase.acquisitionDate.trim()) errors.acquisitionDate = 'Obrigatório.';
                if (!purchase.invoiceNumber.trim()) errors.invoiceNumber = 'Obrigatório.';
                if (!purchase.supplierId) errors.supplierId = 'Obrigatório.';
                if (!purchaseDocFile) errors.purchaseDocFile = 'Cópia da nota de compra é obrigatória.';
            } else {
                if (!purchase.purchaseInfoUnavailableReason.trim()) {
                    errors.purchaseInfoUnavailableReason = 'Informe o motivo.';
                }
            }
        }

        if (step === 3) {
            if (!warranty.warrantyInfoUnavailable && !purchase.purchaseInfoUnavailable) {
                if (!warranty.warrantyMonths.trim() && !warranty.warrantyEndDate.trim()) {
                    errors.warrantyMonths = 'Informe a duração ou a data fim.';
                    errors.warrantyEndDate = 'Informe a duração ou a data fim.';
                }
            }
            if (warranty.warrantyInfoUnavailable && !warranty.warrantyInfoUnavailableReason.trim()) {
                errors.warrantyInfoUnavailableReason = 'Informe o motivo.';
            }
        }

        setStepErrors(errors);
        return Object.keys(errors).length === 0;
    }, [form, purchase, warranty, purchaseDocFile]);

    // ─── Cascade handlers ───
    const handleCompanyChange = (v: string) => { set('companyId', v); set('plantId', ''); set('plant', ''); };
    const handleManufacturerChange = (v: string) => { set('manufacturer', v); set('model', ''); };
    const handleTypeChange = (v: string) => { set('equipmentType', v); set('model', ''); };

    // ─── Step navigation ───
    const handleBack = useCallback(() => {
        if (currentStep === 0) {
            confirmNavigation(() => (onExit ? onExit() : navigate('/it/equipment')));
        } else {
            setCurrentStep(prev => prev - 1);
        }
    }, [currentStep, navigate, confirmNavigation, onExit]);

    const handleNext = useCallback(async () => {
        setGlobalError('');

        // Steps 0-3: validate and advance
        if (currentStep < 4) {
            if (!validateStep(currentStep)) {
                setGlobalError('Preencha os campos obrigatórios corretamente.');
                return;
            }
            setCompletedSteps(prev => new Set(prev).add(currentStep));
            setCurrentStep(prev => prev + 1);
            return;
        }

        // Step 4: Submit
        try {
            setSaving(true);
            setGlobalError('');

            const acquisitionPayload = {
                purchaseAmount: purchase.purchaseInfoUnavailable ? null : (parseFloat(purchase.purchaseAmount.toString()) || null),
                currency: purchase.currency || 'AOA',
                acquisitionDate: purchase.purchaseInfoUnavailable ? null : (purchase.acquisitionDate || null),
                invoiceNumber: purchase.purchaseInfoUnavailable ? null : (purchase.invoiceNumber || null),
                supplierId: purchase.purchaseInfoUnavailable ? null : purchase.supplierId,
                supplierName: purchase.purchaseInfoUnavailable ? null : (purchase.supplierName || null),
                purchaseRequestId: purchase.purchaseRequestId || null,
                purchaseRequestNumber: purchase.purchaseRequestNumber || null,
                purchaseOrderNumber: purchase.purchaseOrderNumber || null,
                purchaseInfoUnavailable: purchase.purchaseInfoUnavailable,
                purchaseInfoUnavailableReason: purchase.purchaseInfoUnavailable ? purchase.purchaseInfoUnavailableReason : null,
                warrantyMonths: warranty.warrantyInfoUnavailable ? null : (parseInt(warranty.warrantyMonths) || null),
                warrantyStartDate: warranty.warrantyInfoUnavailable ? null : (warranty.warrantyStartDate || null),
                warrantyEndDate: warranty.warrantyInfoUnavailable ? null : (warranty.warrantyEndDate || null),
                warrantyNotes: warranty.warrantyNotes || null,
                warrantyInfoUnavailable: warranty.warrantyInfoUnavailable,
                warrantyInfoUnavailableReason: warranty.warrantyInfoUnavailable ? warranty.warrantyInfoUnavailableReason : null,
            };

            const createPayload: any = {
                companyId: Number(form.companyId),
                plantId: Number(form.plantId),
                equipmentType: form.equipmentType,
                hostname: form.hostname || null,
                statusCode: form.statusCode,
                manufacturer: form.manufacturer || null,
                model: form.model || null,
                serialNumber: form.serialNumber || null,
                macAddress: form.macAddress || null,
                wifiMacAddress: form.wifiMacAddress || null,
                processor: form.processor || null,
                memoryRam: form.memoryRam || null,
                color: form.color || null,
                biometricMfaEnabled: form.biometricMfaEnabled,
                idCard: form.idCard || null,
                notes: form.notes || null,
                sourceType: form.sourceType,
                legacyAssetCode: form.legacyAssetCode || null,
                manufactureDate: form.manufactureDate || null,
                acquisition: acquisitionPayload,
            };

            const result = await itEquipmentApi.create(createPayload);

            // Upload purchase document
            if (purchaseDocFile && result.id) {
                try {
                    await itEquipmentApi.documents.upload(result.id, purchaseDocFile, 'PURCHASE_DOCUMENT');
                } catch {
                    setPurchaseDocWarning(
                        `Equipamento criado (${result.assetTag}), mas o documento de compra não foi carregado. ` +
                        `Abra o equipamento para tentar novamente.`
                    );
                }
            }

            setCreationResult(result);
            setIsDirty(false);
            setCompletedSteps(prev => new Set(prev).add(4));
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao criar equipamento.';
            setGlobalError(message);
        } finally {
            setSaving(false);
        }
    }, [currentStep, validateStep, form, purchase, warranty, purchaseDocFile, navigate]);

    // ─── Get label for equipment type ───
    const typeLabel = useMemo(() => {
        const found = equipmentTypes.find(t => t.value === form.equipmentType);
        return found?.label || form.equipmentType;
    }, [form.equipmentType, equipmentTypes]);

    const companyLabel = useMemo(() => {
        const found = companies.find(c => String(c.id) === form.companyId);
        return found?.name || form.companyId;
    }, [form.companyId, companies]);

    const plantLabel = useMemo(() => {
        const found = plants.find(p => String(p.id) === form.plantId);
        return found?.name || form.plant || form.plantId;
    }, [form.plantId, form.plant, plants]);

    // ─── Success Panel ───
    if (creationResult) {
        return (
            <div style={{
                maxWidth: '600px', margin: '80px auto', padding: '40px',
                textAlign: 'center',
            }}>
                <div style={{
                    width: '80px', height: '80px', borderRadius: '50%',
                    background: 'linear-gradient(135deg, #16a34a, #15803d)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    margin: '0 auto 24px',
                    boxShadow: '0 8px 24px rgba(22,163,74,0.3)',
                }}>
                    <CheckCircle size={40} color="#ffffff" />
                </div>

                <h2 style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--color-text)', marginBottom: '8px' }}>
                    Equipamento Criado com Sucesso
                </h2>
                <p style={{ color: 'var(--color-text-muted)', fontSize: '0.9rem', marginBottom: '8px' }}>
                    O equipamento foi registrado no sistema.
                </p>

                <div style={{
                    display: 'inline-block', padding: '12px 24px',
                    background: '#f0fdf4', border: '1px solid #86efac',
                    borderRadius: '10px', marginBottom: '24px',
                }}>
                    <div style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: '4px' }}>
                        Código do Ativo
                    </div>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, fontFamily: 'monospace', color: '#166534', letterSpacing: '1px' }}>
                        {creationResult.assetTag}
                    </div>
                </div>

                {purchaseDocWarning && (
                    <div style={{
                        padding: '12px 16px', backgroundColor: '#fffbeb', border: '1px solid #fde68a',
                        borderRadius: '8px', color: '#92400e', fontSize: '0.85rem', textAlign: 'left',
                        marginBottom: '24px',
                    }}>
                        ⚠️ {purchaseDocWarning}
                    </div>
                )}

                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', maxWidth: '320px', margin: '0 auto' }}>
                    <button
                        onClick={() => navigate(`/it/equipment/${creationResult.id}`)}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', background: 'var(--color-primary)',
                            border: 'none', borderRadius: '8px', color: '#fff', fontSize: '0.9rem',
                            fontWeight: 600, cursor: 'pointer',
                            boxShadow: 'var(--shadow-sm)',
                        }}
                    >
                        <ExternalLink size={16} /> Ver equipamento
                    </button>
                    <button
                        onClick={() => navigate(`/it/equipment/${creationResult.id}/label`)}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)', borderRadius: '8px',
                            color: 'var(--color-text)', fontSize: '0.9rem', fontWeight: 500, cursor: 'pointer',
                        }}
                    >
                        <Printer size={16} /> Imprimir etiqueta
                    </button>
                    <button
                        onClick={() => {
                            // Reset all state for a new creation
                            setCreationResult(null);
                            setPurchaseDocWarning('');
                            setCurrentStep(0);
                            setCompletedSteps(new Set());
                            setStepErrors({});
                            setGlobalError('');
                            setForm({
                                hostname: '', companyId: '', plantId: '', plant: '',
                                equipmentType: 'LAPTOP', statusCode: 'AVAILABLE',
                                manufacturer: '', model: '', serialNumber: '',
                                macAddress: '', wifiMacAddress: '', processor: '',
                                memoryRam: '', color: '', biometricMfaEnabled: false,
                                idCard: '', notes: '', sourceType: 'MANUAL_REGISTRATION',
                                legacyAssetCode: '', manufactureDate: '',
                            });
                            setPurchase({
                                purchaseAmount: '', currency: 'AOA', acquisitionDate: '',
                                supplierId: null, supplierName: '', supplierPortalCode: '',
                                purchaseRequestId: '', purchaseRequestNumber: '',
                                purchaseOrderNumber: '', invoiceNumber: '',
                                purchaseInfoUnavailable: false, purchaseInfoUnavailableReason: '',
                            });
                            setWarranty({
                                warrantyMonths: '', warrantyStartDate: '', warrantyEndDate: '',
                                warrantyNotes: '', warrantyInfoUnavailable: false,
                                warrantyInfoUnavailableReason: '',
                            });
                            setPurchaseDocFile(null);
                        }}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)', borderRadius: '8px',
                            color: 'var(--color-text)', fontSize: '0.9rem', fontWeight: 500, cursor: 'pointer',
                        }}
                    >
                        <Plus size={16} /> Criar outro equipamento
                    </button>
                    <button
                        onClick={() => navigate('/it/equipment')}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', background: 'transparent', border: 'none',
                            color: 'var(--color-text-muted)', fontSize: '0.85rem', fontWeight: 500,
                            cursor: 'pointer',
                        }}
                    >
                        <ArrowRight size={16} /> Voltar ao estoque
                    </button>
                </div>
            </div>
        );
    }

    // ─── Step content renderers ───
    const renderStep0 = () => (
        <SectionCard title="Identificação Básica" icon={<Monitor size={18} />} description="Defina a origem, tipo e localização do equipamento.">
            {globalError && (
                <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#dc2626', fontSize: '0.82rem', marginBottom: 12 }}>
                    {globalError}
                </div>
            )}
            <div style={{ display: 'flex', gap: 12 }}>
                <FormSearchableSelect label="Tipo" value={form.equipmentType} onChange={handleTypeChange}
                    options={equipmentTypes.length > 0 ? equipmentTypes : [{ value: 'UNKNOWN', label: 'Carregando...' }]}
                    error={stepErrors.equipmentType}
                    required
                />
                <FormSelect label="Status" value={form.statusCode} onChange={v => set('statusCode', v)}
                    options={[
                        { value: 'AVAILABLE', label: 'Disponível' },
                        { value: 'IN_USE', label: 'Em uso' },
                        { value: 'IN_REPAIR', label: 'Em conserto' },
                        { value: 'RESERVED', label: 'Reservado' },
                    ]}
                />
            </div>
            <div style={{ display: 'flex', gap: 12 }}>
                <FormSelect label="Empresa *" value={form.companyId} onChange={handleCompanyChange}
                    options={[
                        { value: '', label: 'Selecione...' },
                        ...companies.filter(c => c.isActive).map(c => ({ value: String(c.id), label: c.name }))
                    ]}
                    error={stepErrors.companyId}
                />
                <FormSearchableSelect label="Planta" value={form.plantId} onChange={v => { set('plantId', v); const pl = plants.find(p => String(p.id) === v); if (pl) set('plant', pl.name); }}
                    options={[
                        { value: '', label: form.companyId ? 'Selecione...' : 'Selecione empresa primeiro' },
                        ...plants.filter(p => p.isActive).map(p => ({ value: String(p.id), label: p.name }))
                    ]}
                    disabled={!form.companyId}
                    error={stepErrors.plantId}
                    required
                />
            </div>
            <div style={{ display: 'flex', gap: 12 }}>
                <FormSelect label="Origem do Equipamento" value={form.sourceType} onChange={v => set('sourceType', v)}
                    options={[
                        { value: 'MANUAL_REGISTRATION', label: 'Registo Manual' },
                        { value: 'MANUAL_PURCHASE', label: 'Compra / Aquisição' },
                    ]}
                />
                <FormInput label="Código Legado" value={form.legacyAssetCode} onChange={v => set('legacyAssetCode', v)} placeholder="Código patrimônio antigo (opcional)" />
            </div>
            <FormTextarea label="Notas" value={form.notes} onChange={v => set('notes', v)} rows={3} placeholder="Observações gerais sobre o equipamento (opcional)" />
        </SectionCard>
    );

    const renderStep1 = () => (
        <SectionCard title="Especificações Técnicas" description="Detalhes de fabricante, modelo e hardware do equipamento.">
            {globalError && (
                <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#dc2626', fontSize: '0.82rem', marginBottom: 12 }}>
                    {globalError}
                </div>
            )}
            {/* Asset code preview */}
            <div style={{ marginBottom: 12 }}>
                <div style={{ padding: '8px 12px', background: '#f8fafc', border: '1px dashed #94a3b8', borderRadius: 6, color: '#64748b', fontSize: '0.82rem', fontStyle: 'italic' }}>
                    Código do ativo será gerado automaticamente ao criar.
                </div>
            </div>
            {showHostname && <FormInput label="Hostname" value={form.hostname} onChange={v => set('hostname', v)} />}
            <div style={{ display: 'flex', gap: 12 }}>
                <FormSearchableSelect label="Fabricante" value={form.manufacturer} onChange={handleManufacturerChange}
                    options={manufacturerOpts}
                />
                <FormSearchableSelect label="Modelo" value={form.model} onChange={v => set('model', v)}
                    options={modelOpts}
                    disabled={!form.manufacturer}
                />
            </div>
            <div style={{ display: 'flex', gap: 12 }}>
                <FormInput label="Serial Number" value={form.serialNumber} onChange={v => set('serialNumber', v)} />
                {showMacAddress && <FormInput label="MAC Ethernet" value={form.macAddress} onChange={v => set('macAddress', v)} placeholder="Ex: AA:BB:CC:DD:EE:FF" />}
            </div>
            {showMacAddress && (
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormInput label="MAC Wi-Fi" value={form.wifiMacAddress} onChange={v => set('wifiMacAddress', v)} placeholder="Ex: AA:BB:CC:DD:EE:FF" />
                    <div style={{ flex: 1 }} />
                </div>
            )}
            {showProcessorRam && (
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormSearchableSelect label="Processador" value={form.processor} onChange={v => set('processor', v)} options={processorOpts} />
                    <FormSearchableSelect label="RAM" value={form.memoryRam} onChange={v => set('memoryRam', v)} options={memoryOpts} />
                </div>
            )}
            <div style={{ display: 'flex', gap: 12 }}>
                <FormInput label="Cor" value={form.color} onChange={v => set('color', v)} />
                <FormInput label="ID Card" value={form.idCard} onChange={v => set('idCard', v)} />
            </div>
            <div style={{ display: 'flex', gap: 12 }}>
                <FormInput label="Data de Fabricação" type="date" value={form.manufactureDate} onChange={v => set('manufactureDate', v)} style={{ flex: 1 }} />
                <div style={{ flex: 1, paddingTop: 18 }}><FormCheckbox label="Biometria / MFA" checked={form.biometricMfaEnabled} onChange={v => set('biometricMfaEnabled', v)} id="biocheck-wizard" /></div>
            </div>
        </SectionCard>
    );

    const renderStep2 = () => (
        <SectionCard title="Compra / Rastreabilidade" icon={<span style={{ fontSize: '1rem' }}>📋</span>} description="Informações de aquisição, fornecedor e documento de compra.">
            {globalError && (
                <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#dc2626', fontSize: '0.82rem', marginBottom: 12 }}>
                    {globalError}
                </div>
            )}
            <FormCheckbox label="Informações de compra indisponíveis" checked={purchase.purchaseInfoUnavailable} onChange={v => setPur('purchaseInfoUnavailable', v)} id="purchaseUnavailableWizard" style={{ marginBottom: 12 }} />

            {purchase.purchaseInfoUnavailable ? (
                <FormTextarea label="Motivo da indisponibilidade *" value={purchase.purchaseInfoUnavailableReason}
                    onChange={v => setPur('purchaseInfoUnavailableReason', v)} rows={2}
                    placeholder="Ex: Equipamento adquirido antes da implementação do sistema de rastreabilidade."
                    error={stepErrors.purchaseInfoUnavailableReason}
                />
            ) : (
                <>
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormInput label="Valor de compra *" value={purchase.purchaseAmount.toString()} onChange={v => setPur('purchaseAmount', v)} placeholder="0.00" error={stepErrors.purchaseAmount} />
                        <FormSelect label="Moeda" value={purchase.currency} onChange={v => setPur('currency', v)}
                            options={[
                                { value: 'AOA', label: 'AOA — Kwanza' },
                                { value: 'USD', label: 'USD — Dólar' },
                                { value: 'EUR', label: 'EUR — Euro' },
                            ]}
                        />
                    </div>
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormInput label="Data de compra *" type="date" value={purchase.acquisitionDate} onChange={v => setPur('acquisitionDate', v)} error={stepErrors.acquisitionDate} style={{ flex: 1 }} />
                        <FormInput label="Nº do documento de compra / entrega *" value={purchase.invoiceNumber} onChange={v => setPur('invoiceNumber', v)} placeholder="Fatura, guia, ou documento interno" error={stepErrors.invoiceNumber} />
                    </div>
                    <div style={{ display: 'flex', gap: 12 }}>
                        <div style={{ flex: 1 }}>
                            <label style={{ display: 'block', fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', marginBottom: 4 }}>Fornecedor *</label>
                            <div style={{ border: stepErrors.supplierId ? '1px solid #ef4444' : 'none', borderRadius: 6 }}>
                                <SupplierAutocomplete
                                    initialName={purchase.supplierName}
                                    initialPortalCode={purchase.supplierPortalCode}
                                    onChange={(id, name, portalCode) => {
                                        setPur('supplierId', id);
                                        setPur('supplierName', name);
                                        setPur('supplierPortalCode', portalCode || '');
                                    }}
                                />
                            </div>
                            {stepErrors.supplierId && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{stepErrors.supplierId}</div>}
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-subtle)' }}>Nº Ordem de Compra / Requisição</label>
                            <PurchaseRequestPicker
                                value={purchase.purchaseOrderNumber}
                                requestId={purchase.purchaseRequestId}
                                onChange={(reqId, reqNum, displayVal) => {
                                    setPur('purchaseRequestId', reqId || '');
                                    setPur('purchaseRequestNumber', reqNum || '');
                                    setPur('purchaseOrderNumber', displayVal || '');
                                }}
                            />
                        </div>
                    </div>
                    <div style={{ marginTop: 8 }}>
                        <FileUpload
                            label="Cópia da nota de compra / guia de entrega *"
                            file={purchaseDocFile}
                            onChange={f => { setPurchaseDocFile(f); setIsDirty(true); }}
                            accept=".pdf,.jpg,.jpeg,.png"
                            maxSizeMB={10}
                            error={stepErrors.purchaseDocFile}
                            helperText="PDF, JPG ou PNG — máximo 10 MB"
                        />
                    </div>
                </>
            )}
        </SectionCard>
    );

    const renderStep3 = () => (
        <SectionCard title="Garantia" icon={<span style={{ fontSize: '1rem' }}>🛡️</span>} description="Informações de garantia do equipamento.">
            {globalError && (
                <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#dc2626', fontSize: '0.82rem', marginBottom: 12 }}>
                    {globalError}
                </div>
            )}
            <FormCheckbox label="Informações de garantia indisponíveis" checked={warranty.warrantyInfoUnavailable} onChange={v => setWar('warrantyInfoUnavailable', v)} id="warrantyUnavailableWizard" style={{ marginBottom: 12 }} />

            {warranty.warrantyInfoUnavailable ? (
                <FormTextarea label="Motivo da indisponibilidade *" value={warranty.warrantyInfoUnavailableReason}
                    onChange={v => setWar('warrantyInfoUnavailableReason', v)} rows={2}
                    placeholder="Ex: Informações de garantia não disponíveis — equipamento recebido sem documentação."
                    error={stepErrors.warrantyInfoUnavailableReason}
                />
            ) : (
                <>
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormInput label="Garantia (meses)" value={warranty.warrantyMonths} onChange={v => setWar('warrantyMonths', v)} type="number" placeholder="12" error={stepErrors.warrantyMonths} />
                        <FormInput label="Início da garantia" type="date" value={warranty.warrantyStartDate} onChange={v => setWar('warrantyStartDate', v)} style={{ flex: 1 }} />
                    </div>
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormInput label="Fim da garantia" type="date" value={warranty.warrantyEndDate} onChange={v => setWar('warrantyEndDate', v)} error={stepErrors.warrantyEndDate}
                            helperText={warranty.warrantyMonths && warranty.warrantyEndDate ? `Calculado automaticamente a partir de ${warranty.warrantyMonths} meses. Editável.` : undefined}
                            style={{ flex: 1 }}
                        />
                        <FormInput label="Notas de garantia" value={warranty.warrantyNotes} onChange={v => setWar('warrantyNotes', v)} placeholder="Informações adicionais" />
                    </div>
                </>
            )}
        </SectionCard>
    );

    const renderStep4 = () => {
        const reviewItems: Array<{ label: string; value: string | null }> = [
            { label: 'Tipo', value: typeLabel },
            { label: 'Empresa', value: companyLabel },
            { label: 'Planta', value: plantLabel },
            { label: 'Status', value: form.statusCode },
            { label: 'Origem', value: form.sourceType === 'MANUAL_PURCHASE' ? 'Compra / Aquisição' : 'Registo Manual' },
        ];
        if (form.hostname) reviewItems.push({ label: 'Hostname', value: form.hostname });
        if (form.manufacturer) reviewItems.push({ label: 'Fabricante', value: form.manufacturer });
        if (form.model) reviewItems.push({ label: 'Modelo', value: form.model });
        if (form.serialNumber) reviewItems.push({ label: 'Serial Number', value: form.serialNumber });
        if (form.macAddress) reviewItems.push({ label: 'MAC Ethernet', value: form.macAddress });
        if (form.wifiMacAddress) reviewItems.push({ label: 'MAC Wi-Fi', value: form.wifiMacAddress });
        if (form.processor) reviewItems.push({ label: 'Processador', value: form.processor });
        if (form.memoryRam) reviewItems.push({ label: 'RAM', value: form.memoryRam });
        if (form.color) reviewItems.push({ label: 'Cor', value: form.color });
        if (form.idCard) reviewItems.push({ label: 'ID Card', value: form.idCard });
        if (form.manufactureDate) reviewItems.push({ label: 'Data de Fabricação', value: form.manufactureDate });
        if (form.biometricMfaEnabled) reviewItems.push({ label: 'Biometria / MFA', value: 'Sim' });
        if (form.legacyAssetCode) reviewItems.push({ label: 'Código Legado', value: form.legacyAssetCode });
        if (form.notes) reviewItems.push({ label: 'Notas', value: form.notes });

        return (
            <>
                {globalError && (
                    <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#dc2626', fontSize: '0.82rem', marginBottom: 12 }}>
                        {globalError}
                    </div>
                )}
                <SectionCard title="Dados do Equipamento" description="Revise as informações antes de criar.">
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px 24px' }}>
                        {reviewItems.map(item => (
                            <div key={item.label} style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>{item.label}</span>
                                <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{item.value || '—'}</span>
                            </div>
                        ))}
                    </div>
                </SectionCard>

                <SectionCard title="Compra / Rastreabilidade">
                    {purchase.purchaseInfoUnavailable ? (
                        <div style={{ padding: '8px 12px', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: 6, color: '#92400e', fontSize: '0.85rem' }}>
                            Informações indisponíveis — {purchase.purchaseInfoUnavailableReason}
                        </div>
                    ) : (
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px 24px' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Valor</span>
                                <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{purchase.purchaseAmount} {purchase.currency}</span>
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Data de Compra</span>
                                <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{purchase.acquisitionDate || '—'}</span>
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Fornecedor</span>
                                <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{purchase.supplierName || '—'}</span>
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Nº Documento</span>
                                <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{purchase.invoiceNumber || '—'}</span>
                            </div>
                            {purchase.purchaseOrderNumber && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Ordem de Compra</span>
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{purchase.purchaseOrderNumber}</span>
                                </div>
                            )}
                            {purchaseDocFile && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Documento</span>
                                    <span style={{ fontSize: '0.875rem', color: '#16a34a', fontWeight: 500 }}>✓ {purchaseDocFile.name}</span>
                                </div>
                            )}
                        </div>
                    )}
                </SectionCard>

                <SectionCard title="Garantia">
                    {warranty.warrantyInfoUnavailable ? (
                        <div style={{ padding: '8px 12px', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: 6, color: '#92400e', fontSize: '0.85rem' }}>
                            Informações indisponíveis — {warranty.warrantyInfoUnavailableReason}
                        </div>
                    ) : purchase.purchaseInfoUnavailable ? (
                        <div style={{ padding: '8px 12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 6, color: '#64748b', fontSize: '0.85rem' }}>
                            Garantia não aplicável — informações de compra indisponíveis.
                        </div>
                    ) : (
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px 24px' }}>
                            {warranty.warrantyMonths && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Duração</span>
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{warranty.warrantyMonths} meses</span>
                                </div>
                            )}
                            {warranty.warrantyStartDate && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Início</span>
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{warranty.warrantyStartDate}</span>
                                </div>
                            )}
                            {warranty.warrantyEndDate && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Fim</span>
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{warranty.warrantyEndDate}</span>
                                </div>
                            )}
                            {warranty.warrantyNotes && (
                                <div style={{ display: 'flex', flexDirection: 'column', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.3px' }}>Notas</span>
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text)', fontWeight: 500 }}>{warranty.warrantyNotes}</span>
                                </div>
                            )}
                        </div>
                    )}
                </SectionCard>

                <div style={{
                    padding: '12px 16px', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe',
                    borderRadius: '8px', fontSize: '0.85rem', color: '#1e40af',
                }}>
                    O código do ativo será gerado automaticamente ao criar o equipamento.
                </div>
            </>
        );
    };

    const stepRenderers = [renderStep0, renderStep1, renderStep2, renderStep3, renderStep4];

    return (
        <>
            <WizardLayout
                breadcrumbs={BREADCRUMBS}
                title="Novo Equipamento"
                subtitle={onModeChange ? (
                    <button 
                        onClick={() => {
                            if (!isDirty) onModeChange();
                            else confirmNavigation(() => onModeChange());
                        }}
                        style={{
                            background: 'none', border: 'none', padding: 0,
                            color: 'var(--color-primary)', fontSize: '0.85rem',
                            cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '4px',
                            marginTop: '4px'
                        }}
                    >
                        &larr; Alterar tipo de cadastro
                    </button>
                ) : "Cadastre um novo equipamento de T.I. passo a passo."}
                titleIcon={<Monitor size={28} />}
                steps={STEPS}
                currentStep={currentStep}
                completedSteps={completedSteps}
                onBack={handleBack}
                onNext={handleNext}
                isSubmitting={saving}
                submitLabel="Criar equipamento"
                canProceed={!saving}
            >
                {stepRenderers[currentStep]()}
            </WizardLayout>

            {/* Unsaved data confirmation dialog */}
            {showLeaveDialog && (
                <ConfirmationDialog
                    title="Sair sem salvar?"
                    message="Você possui dados não salvos no formulário de criação. Se sair agora, todas as informações serão perdidas."
                    confirmText="Sair sem salvar"
                    cancelText="Continuar editando"
                    variant="destructive"
                    onConfirm={handleConfirmLeave}
                    onCancel={handleCancelLeave}
                />
            )}
        </>
    );
}
