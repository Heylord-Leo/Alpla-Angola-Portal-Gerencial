import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Layers, CheckCircle, Plus, ArrowRight, ExternalLink } from 'lucide-react';
import { itEquipmentApi, itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import type { ITEquipmentTypeItem, MasterDataCompany, MasterDataPlant, CatalogManufacturer, CatalogModel } from '../../types/itEquipment';
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
import { PurchaseRequestPicker } from '../../components/common/form/PurchaseRequestPicker';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { useUnsavedChangesWarning } from '../../hooks/useUnsavedChangesWarning';
import { labelStyle, inputStyle } from '../../components/it/EquipmentFormModal';

// ─── Step definitions ───
const STEPS: WizardStep[] = [
    { key: 'basics', label: 'Dados do Lote', description: 'Quantidade, tipo, empresa, planta' },
    { key: 'purchase', label: 'Compra / Fornecedor', description: 'Fornecedor, valor, documento' },
    { key: 'warranty', label: 'Garantia', description: 'Duração, datas, notas' },
    { key: 'items', label: 'Itens Individuais', description: 'Serial, hostname, MAC por item' },
    { key: 'review', label: 'Revisão e Criação', description: 'Confirmação final do lote' },
];

const BREADCRUMBS = [
    { label: 'T.I.', to: '/it/equipment' },
    { label: 'Estoque de Equipamentos', to: '/it/equipment' },
    { label: 'Criar Lote' },
];

// ─── Type-sensitivity helpers ───
const TYPES_WITH_HOSTNAME = ['LAPTOP', 'DESKTOP', 'SERVER', 'TABLET', 'ALL_IN_ONE', 'THIN_CLIENT', 'SWITCH', 'ROUTER', 'ACCESS_POINT', 'FIREWALL', 'PRINTER', 'PLOTTER', 'SCANNER'];
const TYPES_WITH_MAC = ['LAPTOP', 'DESKTOP', 'SERVER', 'TABLET', 'ALL_IN_ONE', 'THIN_CLIENT', 'SWITCH', 'ROUTER', 'ACCESS_POINT', 'FIREWALL', 'PRINTER', 'PLOTTER', 'SCANNER'];

export default function BatchEquipmentWizardPage({ onExit, onModeChange }: { onExit?: () => void, onModeChange?: () => void } = {}) {
    const navigate = useNavigate();
    const [currentStep, setCurrentStep] = useState(0);
    const [completedSteps, setCompletedSteps] = useState<Set<number>>(new Set());
    const [isDirty, setIsDirty] = useState(false);
    const [saving, setSaving] = useState(false);
    const [globalError, setGlobalError] = useState('');

    // ── Master data ──
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);
    const [companies, setCompanies] = useState<MasterDataCompany[]>([]);
    const [plants, setPlants] = useState<MasterDataPlant[]>([]);
    const [manufacturers, setManufacturers] = useState<CatalogManufacturer[]>([]);
    const [models, setModels] = useState<CatalogModel[]>([]);

    // ── Form state (shared across batch) ──
    const [form, setForm] = useState({
        companyId: '',
        plantId: '',
        equipmentType: 'LAPTOP',
        statusCode: 'AVAILABLE',
        manufacturer: '',
        model: '',
        color: '',
        manufactureDate: '',
        sourceType: 'MANUAL_PURCHASE',
        notes: '',
        quantity: '2',
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

    // ── Individual items ──
    const [items, setItems] = useState<Array<{ id: number; serialNumber: string; hostname: string; macAddress: string; wifiMacAddress: string; idCard: string; notes: string }>>([
        { id: Date.now(), serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' },
        { id: Date.now() + 1, serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' },
    ]);

    // ── Quantity reduction confirmation ──
    const [pendingQuantity, setPendingQuantity] = useState<string | null>(null);

    // ── Creation result ──
    const [creationResult, setCreationResult] = useState<{ count: number; firstAssetCode: string; lastAssetCode: string; items: any[] } | null>(null);

    // ── Field setters ──
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
    }, []);

    // ── Company → Plant cascade ──
    useEffect(() => {
        if (form.companyId) {
            api.lookups.getPlants(Number(form.companyId)).then(setPlants).catch(() => setPlants([]));
        } else {
            setPlants([]);
        }
    }, [form.companyId]);

    const handleCompanyChange = (v: string) => { set('companyId', v); set('plantId', ''); };

    // ── Manufacturer → Model cascade (filtered by type) ──
    useEffect(() => {
        if (form.manufacturer) {
            const mfr = manufacturers.find(m => m.name === form.manufacturer);
            if (mfr) {
                itEquipmentCatalogApi.models.list({ activeOnly: true, manufacturerId: mfr.id, equipmentTypeCode: form.equipmentType || undefined })
                    .then(setModels).catch(() => setModels([]));
            } else setModels([]);
        } else setModels([]);
    }, [form.manufacturer, form.equipmentType, manufacturers]);

    const handleManufacturerChange = (v: string) => { set('manufacturer', v); set('model', ''); };
    const handleTypeChange = (v: string) => { set('equipmentType', v); set('model', ''); };

    // ── Quantity management ──
    const handleQuantityChange = (newQtyStr: string) => {
        const newQty = parseInt(newQtyStr);
        if (isNaN(newQty)) { set('quantity', newQtyStr); return; }
        const currentQty = parseInt(form.quantity) || 2;
        if (newQty < currentQty) {
            const itemsToRemove = items.slice(newQty);
            const hasData = itemsToRemove.some(it => it.serialNumber || it.hostname || it.macAddress || it.idCard);
            if (hasData) { setPendingQuantity(newQtyStr); return; }
        }
        set('quantity', newQtyStr);
    };

    const confirmQuantityReduction = () => {
        if (pendingQuantity !== null) {
            set('quantity', pendingQuantity);
            setPendingQuantity(null);
        }
    };

    // ── Sync items array with quantity ──
    useEffect(() => {
        const qty = parseInt(form.quantity) || 2;
        setItems(prev => {
            if (prev.length === qty) return prev;
            if (prev.length > qty) return prev.slice(0, qty);
            const add = qty - prev.length;
            const newItems = [...prev];
            for (let i = 0; i < add; i++) {
                newItems.push({ id: Date.now() + i, serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' });
            }
            return newItems;
        });
    }, [form.quantity]);

    const updateItem = (index: number, field: string, value: string) => {
        setItems(prev => {
            const next = [...prev];
            next[index] = { ...next[index], [field]: value };
            return next;
        });
        setIsDirty(true);
    };

    // ── Warranty auto-calculation ──
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
        if (endStr !== warranty.warrantyEndDate) setWar('warrantyEndDate', endStr);
    }, [warranty.warrantyMonths, warranty.warrantyStartDate, purchase.acquisitionDate]);

    // ── Auto-populate warranty start from acquisition date ──
    useEffect(() => {
        if (!warranty.warrantyStartDate && purchase.acquisitionDate) {
            setWar('warrantyStartDate', purchase.acquisitionDate);
        }
    }, [purchase.acquisitionDate]);

    // ── Type-sensitivity booleans ──
    const showHostname = TYPES_WITH_HOSTNAME.includes(form.equipmentType);
    const showMac = TYPES_WITH_MAC.includes(form.equipmentType);

    // ─── Per-step validation ───
    const validateStep = useCallback((step: number): string | null => {
        switch (step) {
            case 0: {
                const qty = parseInt(form.quantity);
                if (isNaN(qty) || qty < 2 || qty > 100) return 'O lote deve conter no mínimo 2 itens (máx. 100). Para criar apenas 1 equipamento, utilize o cadastro individual.';
                if (!form.companyId) return 'Empresa é obrigatória.';
                if (!form.plantId) return 'Planta é obrigatória.';
                if (!form.equipmentType) return 'Tipo de equipamento é obrigatório.';
                return null;
            }
            case 1: {
                if (!purchase.purchaseInfoUnavailable) {
                    if (!purchase.purchaseAmount.toString().trim()) return 'Valor unitário de compra é obrigatório.';
                    if (!purchase.acquisitionDate.trim()) return 'Data de aquisição é obrigatória.';
                    if (!purchase.invoiceNumber.trim()) return 'Número do documento é obrigatório.';
                    if (!purchase.supplierId) return 'Fornecedor é obrigatório quando as informações de compra estão disponíveis.';
                    if (!purchaseDocFile) return 'Carregue a cópia da nota de compra / guia de entrega.';
                } else {
                    if (!purchase.purchaseInfoUnavailableReason.trim()) return 'Informe o motivo da indisponibilidade das informações de compra.';
                }
                return null;
            }
            case 2: {
                if (!warranty.warrantyInfoUnavailable && !purchase.purchaseInfoUnavailable) {
                    if (!warranty.warrantyMonths.trim() && !warranty.warrantyEndDate.trim()) return 'Informe a garantia (meses) ou a data de fim, ou marque como indisponível.';
                } else if (warranty.warrantyInfoUnavailable) {
                    if (!warranty.warrantyInfoUnavailableReason.trim()) return 'Informe o motivo da indisponibilidade das informações de garantia.';
                }
                return null;
            }
            default:
                return null;
        }
    }, [form, purchase, warranty, purchaseDocFile]);

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

        // Validate current step
        const error = validateStep(currentStep);
        if (error) { setGlobalError(error); return; }

        if (currentStep < STEPS.length - 1) {
            setCompletedSteps(prev => new Set(prev).add(currentStep));
            setCurrentStep(prev => prev + 1);
        } else {
            // ── Final step: submit ──
            setSaving(true);
            try {
                const formData = new FormData();
                formData.append('companyId', form.companyId);
                formData.append('plantId', form.plantId);
                formData.append('equipmentType', form.equipmentType);
                formData.append('statusCode', form.statusCode);
                if (form.manufacturer) formData.append('manufacturer', form.manufacturer);
                if (form.model) formData.append('model', form.model);
                if (form.color) formData.append('color', form.color);
                if (form.manufactureDate) formData.append('manufactureDate', form.manufactureDate);
                if (form.sourceType) formData.append('sourceType', form.sourceType);
                if (form.notes) formData.append('notes', form.notes);
                formData.append('quantity', form.quantity);

                formData.append('purchaseAmount', purchase.purchaseAmount);
                formData.append('currency', purchase.currency);
                formData.append('acquisitionDate', purchase.acquisitionDate);
                if (purchase.supplierId) formData.append('supplierId', String(purchase.supplierId));
                formData.append('supplierName', purchase.supplierName);
                if (purchase.purchaseRequestId) formData.append('purchaseRequestId', purchase.purchaseRequestId);
                formData.append('purchaseRequestNumber', purchase.purchaseRequestNumber);
                formData.append('purchaseOrderNumber', purchase.purchaseOrderNumber);
                formData.append('invoiceNumber', purchase.invoiceNumber);
                formData.append('purchaseInfoUnavailable', String(purchase.purchaseInfoUnavailable));
                formData.append('purchaseInfoUnavailableReason', purchase.purchaseInfoUnavailableReason);

                formData.append('warrantyMonths', warranty.warrantyMonths);
                formData.append('warrantyStartDate', warranty.warrantyStartDate);
                formData.append('warrantyEndDate', warranty.warrantyEndDate);
                formData.append('warrantyNotes', warranty.warrantyNotes);
                formData.append('warrantyInfoUnavailable', String(warranty.warrantyInfoUnavailable));
                formData.append('warrantyInfoUnavailableReason', warranty.warrantyInfoUnavailableReason);

                if (purchaseDocFile) formData.append('purchaseDocument', purchaseDocFile);

                formData.append('itemsJson', JSON.stringify(items.map((it, i) => ({
                    index: i + 1,
                    serialNumber: it.serialNumber,
                    hostname: it.hostname,
                    macAddress: it.macAddress,
                    wifiMacAddress: it.wifiMacAddress,
                    idCard: it.idCard,
                    notes: it.notes,
                }))));

                const result = await itEquipmentApi.createBatch(formData);
                setCreationResult(result);
                setCompletedSteps(prev => new Set(prev).add(currentStep));
            } catch (err: any) {
                setGlobalError(err.message || 'Falha ao criar lote de equipamentos.');
            } finally {
                setSaving(false);
            }
        }
    }, [currentStep, validateStep, form, purchase, warranty, purchaseDocFile, items]);

    // ─── Reset for "create another batch" ───
    const resetWizard = useCallback(() => {
        setForm({ companyId: '', plantId: '', equipmentType: 'LAPTOP', statusCode: 'AVAILABLE', manufacturer: '', model: '', color: '', manufactureDate: '', sourceType: 'MANUAL_PURCHASE', notes: '', quantity: '2' });
        setPurchase({ purchaseAmount: '', currency: 'AOA', acquisitionDate: '', supplierId: null, supplierName: '', supplierPortalCode: '', purchaseRequestId: '', purchaseRequestNumber: '', purchaseOrderNumber: '', invoiceNumber: '', purchaseInfoUnavailable: false, purchaseInfoUnavailableReason: '' });
        setWarranty({ warrantyMonths: '', warrantyStartDate: '', warrantyEndDate: '', warrantyNotes: '', warrantyInfoUnavailable: false, warrantyInfoUnavailableReason: '' });
        setPurchaseDocFile(null);
        setItems([
            { id: Date.now(), serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' },
            { id: Date.now() + 1, serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' },
        ]);
        setCreationResult(null);
        setCurrentStep(0);
        setCompletedSteps(new Set());
        setIsDirty(false);
        setGlobalError('');
    }, []);

    // ─── Computed helpers ───
    const typeLabel = useMemo(() => equipmentTypes.find(t => t.value === form.equipmentType)?.label || form.equipmentType, [equipmentTypes, form.equipmentType]);
    const qtyInt = parseInt(form.quantity) || 2;
    const pAmt = parseFloat(purchase.purchaseAmount);
    const unitAmountFormatted = !isNaN(pAmt) ? pAmt.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—';
    const totalAmountFormatted = !isNaN(pAmt) ? (pAmt * qtyInt).toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—';

    // ─── Step renderers ───

    const renderStep0 = () => (
        <SectionCard title="Dados do Lote" icon={<Layers size={18} />} description="Defina a quantidade e as características comuns do lote.">
            {/* Quantity highlight */}
            <div style={{ padding: '16px', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px', marginBottom: '20px' }}>
                <p style={{ margin: '0 0 12px 0', fontSize: '14px', color: '#1e3a8a', fontWeight: 500 }}>
                    Use esta tela para cadastrar 2 ou mais equipamentos iguais. Para cadastrar apenas 1 equipamento, use "Novo Equipamento".
                </p>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <label style={{ ...labelStyle, fontSize: '14px', fontWeight: 600, color: '#1e3a8a', marginBottom: 0 }}>Quantidade de equipamentos *</label>
                    <input
                        type="number" min="2" max="100"
                        value={form.quantity}
                        onChange={e => handleQuantityChange(e.target.value)}
                        style={{ ...inputStyle, width: '100px', fontSize: '15px', fontWeight: 'bold' }}
                    />
                </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                <FormSelect label="Empresa *" value={form.companyId} onChange={handleCompanyChange}
                    options={[{ value: '', label: 'Selecione...' }, ...companies.map(c => ({ value: String(c.id), label: c.name }))]} />
                <FormSearchableSelect label="Planta" value={form.plantId} onChange={v => set('plantId', v)}
                    options={[{ value: '', label: 'Selecione...' }, ...plants.map(p => ({ value: String(p.id), label: p.name }))]} required />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                <FormSearchableSelect label="Tipo" value={form.equipmentType} onChange={handleTypeChange} options={equipmentTypes} required />
                <FormSelect label="Status *" value={form.statusCode} onChange={v => set('statusCode', v)}
                    options={[
                        { value: 'AVAILABLE', label: 'Disponível' },
                        { value: 'IN_USE', label: 'Em Uso' },
                        { value: 'IN_REPAIR', label: 'No Conserto' },
                    ]} />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                <FormSearchableSelect label="Fabricante (Catálogo)" value={form.manufacturer} onChange={handleManufacturerChange}
                    options={[{ value: '', label: 'Livre / Outro...' }, ...manufacturers.map(m => ({ value: m.name, label: m.name }))]} />
                <FormSearchableSelect label="Modelo (Catálogo)" value={form.model} onChange={v => set('model', v)} disabled={!form.manufacturer}
                    options={[{ value: '', label: 'Selecione o modelo...' }, ...models.map(m => ({ value: m.name, label: m.name }))]} />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                <FormInput label="Data de Fabrico" type="date" value={form.manufactureDate} onChange={v => set('manufactureDate', v)} />
                <FormInput label="Cor" value={form.color} onChange={v => set('color', v)} />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                <FormSelect label="Origem / Entrada *" value={form.sourceType} onChange={v => set('sourceType', v)}
                    options={[
                        { value: 'MANUAL_PURCHASE', label: 'Nova Compra' },
                        { value: 'MANUAL_REGISTRATION', label: 'Registo Manual / Estoque Antigo' },
                        { value: 'IMPORTED_LEGACY', label: 'Importado de Sistema Legado' },
                    ]} />
                <div />
            </div>
            <div style={{ marginTop: '16px' }}>
                <FormTextarea label="Observações do Lote (Comum)" value={form.notes} onChange={v => set('notes', v)} rows={2} />
            </div>
        </SectionCard>
    );

    const renderStep1 = () => (
        <SectionCard title="Compra / Fornecedor" icon={<span style={{ fontSize: '1rem' }}>📋</span>} description="Informações de compra e fornecedor para todo o lote.">
            <FormCheckbox label="Informações de compra indisponíveis" checked={purchase.purchaseInfoUnavailable}
                onChange={e => { setPur('purchaseInfoUnavailable', e); if (e) setWar('warrantyInfoUnavailable', true); }} id="batchPurchaseUnavailable" style={{ marginBottom: 16 }} />

            {!purchase.purchaseInfoUnavailable ? (
                <>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                        <FormInput label="Data de Aquisição *" type="date" value={purchase.acquisitionDate} onChange={v => setPur('acquisitionDate', v)} />
                        <div style={{ display: 'flex', gap: 8 }}>
                            <FormInput label="Valor Unitário *" type="number" value={purchase.purchaseAmount} onChange={v => setPur('purchaseAmount', v)} style={{ flex: 2 }} />
                            <FormSelect label="Moeda" value={purchase.currency} onChange={v => setPur('currency', v)}
                                options={[{ value: 'AOA', label: 'AOA' }, { value: 'USD', label: 'USD' }, { value: 'EUR', label: 'EUR' }]} style={{ flex: 1 }} />
                        </div>
                    </div>

                    {/* Batch total display */}
                    {!isNaN(pAmt) && pAmt > 0 && (
                        <div style={{ padding: '10px 14px', backgroundColor: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '8px', marginTop: '12px', fontSize: '13px', color: '#166534' }}>
                            <strong>Valor total do lote:</strong> {totalAmountFormatted} {purchase.currency} ({form.quantity} × {unitAmountFormatted} {purchase.currency})
                        </div>
                    )}

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                        <FormInput label="Nº Nota / Guia *" value={purchase.invoiceNumber} onChange={v => setPur('invoiceNumber', v)} />
                        <div>
                            <label style={labelStyle}>Fornecedor *</label>
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
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
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
                        <FileUpload label="Documento da Compra *" file={purchaseDocFile} onChange={f => { setPurchaseDocFile(f); setIsDirty(true); }} accept=".pdf,.jpg,.jpeg,.png" maxSizeMB={10} />
                    </div>
                </>
            ) : (
                <FormTextarea label="Motivo da indisponibilidade (Compra) *" value={purchase.purchaseInfoUnavailableReason} onChange={v => setPur('purchaseInfoUnavailableReason', v)} rows={3} />
            )}
        </SectionCard>
    );

    const renderStep2 = () => (
        <SectionCard title="Garantia" icon={<span style={{ fontSize: '1rem' }}>🛡️</span>} description="Informações de garantia aplicadas a todos os itens do lote.">
            {purchase.purchaseInfoUnavailable ? (
                <>
                    <div style={{ padding: '12px', backgroundColor: '#fef3c7', border: '1px solid #fcd34d', borderRadius: '8px', marginBottom: '16px', fontSize: '13px', color: '#92400e' }}>
                        As informações de garantia estão indisponíveis porque as informações de compra foram marcadas como indisponíveis.
                    </div>
                    <FormTextarea label="Motivo da indisponibilidade (Garantia) *" value={warranty.warrantyInfoUnavailableReason} onChange={v => setWar('warrantyInfoUnavailableReason', v)} rows={2} />
                </>
            ) : (
                <>
                    <FormCheckbox label="Informações de garantia indisponíveis" checked={warranty.warrantyInfoUnavailable}
                        onChange={e => setWar('warrantyInfoUnavailable', e)} id="batchWarrantyUnavailable" style={{ marginBottom: 16 }} />

                    {!warranty.warrantyInfoUnavailable ? (
                        <>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                <FormInput label="Garantia (Meses)" type="number" value={warranty.warrantyMonths} onChange={v => setWar('warrantyMonths', v)} />
                                <FormInput label="Início da Garantia" type="date" value={warranty.warrantyStartDate} onChange={v => setWar('warrantyStartDate', v)} />
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginTop: '16px' }}>
                                <FormInput label="Fim da Garantia" type="date" value={warranty.warrantyEndDate} onChange={v => setWar('warrantyEndDate', v)} />
                                <div />
                            </div>
                            <div style={{ marginTop: '16px' }}>
                                <FormTextarea label="Notas da Garantia" value={warranty.warrantyNotes} onChange={v => setWar('warrantyNotes', v)} rows={2} />
                            </div>
                        </>
                    ) : (
                        <FormTextarea label="Motivo da indisponibilidade (Garantia) *" value={warranty.warrantyInfoUnavailableReason} onChange={v => setWar('warrantyInfoUnavailableReason', v)} rows={2} />
                    )}
                </>
            )}
        </SectionCard>
    );

    const renderStep3 = () => (
        <SectionCard title="Itens Individuais" icon={<span style={{ fontSize: '1rem' }}>📦</span>} description="Dados específicos para cada equipamento do lote. Todos os campos são opcionais.">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                <div style={{ fontSize: '13px', color: '#4b5563', fontWeight: 500 }}>
                    Itens individuais: <span style={{ color: '#1e3a8a', fontWeight: 'bold' }}>{form.quantity}</span>
                </div>
            </div>

            <div style={{ maxHeight: '450px', overflowY: 'auto', border: '1px solid var(--color-border, #e5e7eb)', borderRadius: '8px' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '13px' }}>
                    <thead style={{ position: 'sticky', top: 0, backgroundColor: 'var(--color-bg-surface, #f9fafb)', zIndex: 1, boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                        <tr>
                            <th style={{ padding: '12px', borderBottom: '1px solid var(--color-border, #e5e7eb)', width: '40px' }}>#</th>
                            <th style={{ padding: '12px', borderBottom: '1px solid var(--color-border, #e5e7eb)' }}>Nº Série</th>
                            {showHostname && <th style={{ padding: '12px', borderBottom: '1px solid var(--color-border, #e5e7eb)' }}>Hostname</th>}
                            {showMac && <th style={{ padding: '12px', borderBottom: '1px solid var(--color-border, #e5e7eb)' }}>MAC Address</th>}
                            <th style={{ padding: '12px', borderBottom: '1px solid var(--color-border, #e5e7eb)' }}>ID Card</th>
                        </tr>
                    </thead>
                    <tbody>
                        {items.map((item, idx) => (
                            <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border, #e5e7eb)' }}>
                                <td style={{ padding: '12px', color: '#6b7280' }}>{idx + 1}</td>
                                <td style={{ padding: '8px' }}>
                                    <input value={item.serialNumber} onChange={e => updateItem(idx, 'serialNumber', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                </td>
                                {showHostname && (
                                    <td style={{ padding: '8px' }}>
                                        <input value={item.hostname} onChange={e => updateItem(idx, 'hostname', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                    </td>
                                )}
                                {showMac && (
                                    <td style={{ padding: '8px' }}>
                                        <input value={item.macAddress} onChange={e => updateItem(idx, 'macAddress', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                    </td>
                                )}
                                <td style={{ padding: '8px' }}>
                                    <input value={item.idCard} onChange={e => updateItem(idx, 'idCard', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </SectionCard>
    );

    const renderStep4 = () => (
        <SectionCard title="Revisão e Criação do Lote" icon={<Layers size={18} />} description="Revise o resumo do lote antes de confirmar a criação.">
            {/* Batch Summary */}
            <div style={{ padding: '16px', background: 'var(--color-bg-surface, #f8fafc)', border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '8px', marginBottom: '20px' }}>
                <h4 style={{ margin: '0 0 12px 0', fontSize: '14px', fontWeight: 600, color: 'var(--color-text, #0f172a)' }}>Resumo do Lote</h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', fontSize: '13px' }}>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Quantidade:</strong> {form.quantity} equipamentos</div>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Tipo:</strong> {typeLabel}</div>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Empresa:</strong> {companies.find(c => String(c.id) === form.companyId)?.name || '—'}</div>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Planta:</strong> {plants.find(p => String(p.id) === form.plantId)?.name || '—'}</div>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Fabricante:</strong> {form.manufacturer || '—'}</div>
                    <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Modelo:</strong> {form.model || '—'}</div>
                </div>
            </div>

            {/* Purchase summary */}
            <div style={{ padding: '16px', background: 'var(--color-bg-surface, #f8fafc)', border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '8px', marginBottom: '20px' }}>
                <h4 style={{ margin: '0 0 12px 0', fontSize: '14px', fontWeight: 600, color: 'var(--color-text, #0f172a)' }}>Compra</h4>
                {purchase.purchaseInfoUnavailable ? (
                    <p style={{ margin: 0, fontSize: '13px', color: '#92400e', fontStyle: 'italic' }}>Indisponível — {purchase.purchaseInfoUnavailableReason}</p>
                ) : (
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', fontSize: '13px' }}>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Valor unitário:</strong> {unitAmountFormatted} {purchase.currency}</div>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Total do lote:</strong> {totalAmountFormatted} {purchase.currency}</div>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Data de aquisição:</strong> {purchase.acquisitionDate || '—'}</div>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Nº documento:</strong> {purchase.invoiceNumber || '—'}</div>
                        <div style={{ gridColumn: '1 / -1' }}><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Fornecedor:</strong> {purchase.supplierName || '—'}</div>
                        {purchaseDocFile && <div style={{ gridColumn: '1 / -1' }}><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Documento:</strong> {purchaseDocFile.name}</div>}
                    </div>
                )}
            </div>

            {/* Warranty summary */}
            <div style={{ padding: '16px', background: 'var(--color-bg-surface, #f8fafc)', border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '8px', marginBottom: '20px' }}>
                <h4 style={{ margin: '0 0 12px 0', fontSize: '14px', fontWeight: 600, color: 'var(--color-text, #0f172a)' }}>Garantia</h4>
                {warranty.warrantyInfoUnavailable ? (
                    <p style={{ margin: 0, fontSize: '13px', color: '#92400e', fontStyle: 'italic' }}>Indisponível — {warranty.warrantyInfoUnavailableReason}</p>
                ) : (
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', fontSize: '13px' }}>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Meses:</strong> {warranty.warrantyMonths || '—'}</div>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Início:</strong> {warranty.warrantyStartDate || '—'}</div>
                        <div><strong style={{ color: 'var(--color-text-muted, #64748b)' }}>Fim:</strong> {warranty.warrantyEndDate || '—'}</div>
                    </div>
                )}
            </div>

            {/* Items summary */}
            <div style={{ padding: '16px', background: 'var(--color-bg-surface, #f8fafc)', border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '8px', marginBottom: '20px' }}>
                <h4 style={{ margin: '0 0 12px 0', fontSize: '14px', fontWeight: 600, color: 'var(--color-text, #0f172a)' }}>Itens Individuais ({items.length})</h4>
                {items.some(it => it.serialNumber || it.hostname || it.macAddress || it.idCard) ? (
                    <div style={{ maxHeight: '200px', overflowY: 'auto', fontSize: '13px' }}>
                        {items.map((it, idx) => {
                            const parts = [it.serialNumber && `SN: ${it.serialNumber}`, it.hostname && `Host: ${it.hostname}`, it.macAddress && `MAC: ${it.macAddress}`, it.idCard && `ID: ${it.idCard}`].filter(Boolean);
                            if (parts.length === 0) return null;
                            return (
                                <div key={idx} style={{ padding: '4px 0', borderBottom: '1px solid var(--color-border, #f1f5f9)' }}>
                                    <strong style={{ color: 'var(--color-text-muted, #64748b)' }}>#{idx + 1}:</strong> {parts.join(' · ')}
                                </div>
                            );
                        })}
                    </div>
                ) : (
                    <p style={{ margin: 0, fontSize: '13px', color: 'var(--color-text-muted, #94a3b8)', fontStyle: 'italic' }}>Nenhum dado individual informado. Os equipamentos serão criados apenas com os dados comuns.</p>
                )}
            </div>

            {/* Info box */}
            <div style={{ fontSize: '13px', color: '#4b5563', padding: '12px', backgroundColor: '#f3f4f6', borderRadius: '6px' }}>
                <ul style={{ margin: 0, paddingLeft: '20px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                    <li>Cada equipamento receberá o seu próprio <strong>código de ativo</strong>.</li>
                    <li>Cada equipamento terá o seu próprio <strong>histórico</strong>.</li>
                    <li>Cada equipamento terá um <strong>ciclo de entrega/devolução independente</strong>.</li>
                    <li>A mesma nota/guia de compra será vinculada como referência para todos os itens.</li>
                </ul>
            </div>
        </SectionCard>
    );

    const stepRenderers = [renderStep0, renderStep1, renderStep2, renderStep3, renderStep4];

    // ─── Success panel (post-creation) ───
    if (creationResult) {
        return (
            <div style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                minHeight: 'calc(100vh - 120px)', padding: '40px', textAlign: 'center',
            }}>
                <div style={{
                    width: '72px', height: '72px', borderRadius: '50%', backgroundColor: '#dcfce7',
                    display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: '24px',
                }}>
                    <CheckCircle size={36} color="#16a34a" />
                </div>
                <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--color-text, #111827)', margin: '0 0 8px 0' }}>
                    Lote criado com sucesso!
                </h1>
                <p style={{ fontSize: '1rem', color: 'var(--color-text-muted, #6b7280)', margin: '0 0 8px 0' }}>
                    <strong>{creationResult.count}</strong> equipamentos foram criados.
                </p>

                {/* Asset code range badge */}
                <div style={{
                    display: 'inline-flex', alignItems: 'center', gap: '8px', padding: '10px 20px',
                    backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px',
                    marginBottom: '32px', fontSize: '0.95rem', fontWeight: 600, color: '#1e40af',
                }}>
                    {creationResult.firstAssetCode} → {creationResult.lastAssetCode}
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', width: '100%', maxWidth: '340px' }}>
                    <button
                        onClick={() => navigate('/it/equipment')}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', background: 'var(--color-primary)',
                            border: 'none', borderRadius: '10px', color: '#fff', fontSize: '0.9rem', fontWeight: 600,
                            cursor: 'pointer', transition: 'all 0.2s', boxShadow: 'var(--shadow-sm)',
                        }}
                    >
                        <ArrowRight size={16} /> Voltar ao estoque
                    </button>
                    <button
                        onClick={resetWizard}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 24px', backgroundColor: 'var(--color-bg-surface, #f8fafc)',
                            border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '10px',
                            color: 'var(--color-text, #374151)', fontSize: '0.9rem', fontWeight: 600,
                            cursor: 'pointer', transition: 'all 0.2s',
                        }}
                    >
                        <Plus size={16} /> Criar outro lote
                    </button>
                    {creationResult.items && creationResult.items.length > 0 && (
                        <button
                            onClick={() => navigate(`/it/equipment/${creationResult.items[0].id}`)}
                            style={{
                                display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                                padding: '12px 24px', backgroundColor: 'transparent',
                                border: '1px solid var(--color-border, #e2e8f0)', borderRadius: '10px',
                                color: 'var(--color-text-muted, #6b7280)', fontSize: '0.9rem', fontWeight: 500,
                                cursor: 'pointer', transition: 'all 0.2s',
                            }}
                        >
                            <ExternalLink size={16} /> Ver primeiro equipamento
                        </button>
                    )}
                </div>
            </div>
        );
    }

    return (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
            <WizardLayout
                breadcrumbs={BREADCRUMBS}
                title="Novo Equipamento - Lote"
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
                ) : "Cadastre vários equipamentos semelhantes de uma só vez."}
                titleIcon={<Layers size={28} />}
                steps={STEPS}
                currentStep={currentStep}
                completedSteps={completedSteps}
                onBack={handleBack}
                onNext={handleNext}
                isSubmitting={saving}
                submitLabel={`Confirmar criação de ${form.quantity} equipamentos`}
                canProceed={!saving}
            >
                {globalError && (
                    <div style={{ padding: '12px 16px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', marginBottom: '16px', color: '#991b1b', fontSize: '0.875rem' }}>
                        {globalError}
                    </div>
                )}
                {stepRenderers[currentStep]()}
            </WizardLayout>

            {/* Unsaved data confirmation dialog */}
            {showLeaveDialog && (
                <ConfirmationDialog
                    title="Sair sem salvar?"
                    message="Você possui dados não salvos no formulário de criação de lote. Se sair agora, todas as informações serão perdidas."
                    confirmText="Sair sem salvar"
                    cancelText="Continuar editando"
                    variant="destructive"
                    onConfirm={handleConfirmLeave}
                    onCancel={handleCancelLeave}
                />
            )}

            {/* Quantity reduction confirmation dialog */}
            {pendingQuantity !== null && (
                <ConfirmationDialog
                    title="Confirmar redução"
                    message="Deseja reduzir a quantidade? Dados inseridos nas últimas linhas serão perdidos."
                    confirmText="Confirmar redução"
                    cancelText="Cancelar"
                    variant="destructive"
                    onConfirm={confirmQuantityReduction}
                    onCancel={() => setPendingQuantity(null)}
                />
            )}
        </div>
    );
}
