import React, { useState, useEffect } from 'react';
import { Loader2 } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { itEquipmentApi, itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import type { ITEquipmentDetail, ITEquipmentTypeItem, MasterDataCompany, MasterDataPlant, CatalogManufacturer, CatalogModel, CatalogProcessor, CatalogMemoryOption } from '../../types/itEquipment';
import { SupplierAutocomplete } from '../SupplierAutocomplete';
import { FormInput } from '../common/form/FormInput';
import { FormSelect } from '../common/form/FormSelect';
import { FormSearchableSelect } from '../common/form/FormSearchableSelect';
import { FormTextarea } from '../common/form/FormTextarea';
import { FormCheckbox } from '../common/form/FormCheckbox';
import { FileUpload } from '../common/form/FileUpload';
import { SectionCard } from '../common/ui/SectionCard';

interface Props {
    equipment?: ITEquipmentDetail;
    onClose: () => void;
    onSuccess: () => void;
}

export function EquipmentFormModal({ equipment, onClose, onSuccess }: Props) {
    const isEdit = !!equipment;
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);

    // ── Master Data lookups ──
    const [companies, setCompanies] = useState<MasterDataCompany[]>([]);
    const [plants, setPlants] = useState<MasterDataPlant[]>([]);
    const [manufacturers, setManufacturers] = useState<CatalogManufacturer[]>([]);
    const [models, setModels] = useState<CatalogModel[]>([]);
    const [processors, setProcessors] = useState<CatalogProcessor[]>([]);
    const [memoryOptions, setMemoryOptions] = useState<CatalogMemoryOption[]>([]);

    // Load dynamic equipment types
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
    }, []);

    // Load companies & catalogs on mount
    useEffect(() => {
        api.lookups.getCompanies().then(setCompanies).catch((err: unknown) => console.error('[EquipmentForm] Failed to load companies:', err));
        itEquipmentCatalogApi.manufacturers.list(true).then(setManufacturers).catch((err: unknown) => console.error('[EquipmentForm] Failed to load manufacturers:', err));
        itEquipmentCatalogApi.processors.list(true).then(setProcessors).catch((err: unknown) => console.error('[EquipmentForm] Failed to load processors:', err));
        itEquipmentCatalogApi.memoryOptions.list(true).then(setMemoryOptions).catch((err: unknown) => console.error('[EquipmentForm] Failed to load memory options:', err));
    }, []);

    const [form, setForm] = useState({
        hostname: equipment?.hostname || '',
        companyId: equipment?.companyId ? String(equipment.companyId) : '',
        plantId: equipment?.plantId ? String(equipment.plantId) : '',
        plant: equipment?.plant || '',
        equipmentType: equipment?.equipmentType || 'LAPTOP',
        statusCode: equipment?.statusCode || 'AVAILABLE',
        manufacturer: equipment?.manufacturer || '',
        model: equipment?.model || '',
        serialNumber: equipment?.serialNumber || '',
        macAddress: equipment?.macAddress || '',
        wifiMacAddress: equipment?.wifiMacAddress || '',
        processor: equipment?.processor || '',
        memoryRam: equipment?.memoryRam || '',
        color: equipment?.color || '',
        biometricMfaEnabled: equipment?.biometricMfaEnabled || false,
        idCard: equipment?.idCard || '',
        notes: equipment?.notes || '',
        sourceType: equipment?.sourceType || 'MANUAL_REGISTRATION',
        legacyAssetCode: equipment?.legacyAssetCode || '',
        manufactureDate: equipment?.manufactureDate ? equipment.manufactureDate.split('T')[0] : '',
    });

    // Purchase / traceability state — visible for both create and edit, independent of SourceType
    const [purchase, setPurchase] = useState({
        purchaseAmount: equipment?.acquisition?.purchaseAmount?.toString() || '',
        currency: equipment?.acquisition?.currency || 'AOA',
        acquisitionDate: equipment?.acquisition?.acquisitionDate ? equipment.acquisition.acquisitionDate.split('T')[0] : '',
        supplierId: equipment?.acquisition?.supplierId || null,
        supplierName: equipment?.acquisition?.supplierName || '',
        supplierPortalCode: equipment?.acquisition?.supplierPortalCode || '',
        purchaseOrderNumber: equipment?.acquisition?.purchaseOrderNumber || '',
        invoiceNumber: equipment?.acquisition?.invoiceNumber || '',
        purchaseInfoUnavailable: equipment?.acquisition?.purchaseInfoUnavailable ?? false,
        purchaseInfoUnavailableReason: equipment?.acquisition?.purchaseInfoUnavailableReason || '',
    });

    // Warranty state
    const [warranty, setWarranty] = useState({
        warrantyMonths: equipment?.acquisition?.warrantyMonths?.toString() || '',
        warrantyStartDate: equipment?.acquisition?.warrantyStartDate ? equipment.acquisition.warrantyStartDate.split('T')[0] : '',
        warrantyEndDate: equipment?.acquisition?.warrantyEndDate ? equipment.acquisition.warrantyEndDate.split('T')[0] : '',
        warrantyNotes: equipment?.acquisition?.warrantyNotes || '',
        warrantyInfoUnavailable: equipment?.acquisition?.warrantyInfoUnavailable ?? false,
        warrantyInfoUnavailableReason: equipment?.acquisition?.warrantyInfoUnavailableReason || '',
    });

    // Purchase document file (for create flow)
    const [purchaseDocFile, setPurchaseDocFile] = useState<File | null>(null);
    const [purchaseDocError, setPurchaseDocError] = useState('');

    const set = (field: string, value: any) => setForm(prev => ({ ...prev, [field]: value }));
    const setPur = (field: string, value: any) => setPurchase(prev => ({ ...prev, [field]: value }));
    const setWar = (field: string, value: any) => setWarranty(prev => ({ ...prev, [field]: value }));

    // Auto-calculate warranty end date from start + months
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

    // Auto-populate warranty start date from acquisition date
    useEffect(() => {
        if (!warranty.warrantyStartDate && purchase.acquisitionDate) {
            setWarranty(prev => ({ ...prev, warrantyStartDate: purchase.acquisitionDate }));
        }
    }, [purchase.acquisitionDate]);

    useEffect(() => {
        if (form.companyId) {
            api.lookups.getPlants(Number(form.companyId)).then(setPlants).catch(() => setPlants([]));
        } else {
            setPlants([]);
        }
    }, [form.companyId]);

    const handleCompanyChange = (v: string) => {
        set('companyId', v);
        set('plantId', ''); // Clear plant when company changes
        set('plant', '');
    };

    // ── Manufacturer → Model cascade ──
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

    const handleManufacturerChange = (v: string) => {
        set('manufacturer', v);
        set('model', ''); // Clear model when manufacturer changes
    };

    const handleTypeChange = (v: string) => {
        set('equipmentType', v);
        set('model', ''); // Clear model when type changes
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        const newErrors: Record<string, string> = {};

        if (!isEdit && !form.companyId) newErrors.companyId = 'Obrigatório.';
        if (!isEdit && !form.plantId) newErrors.plantId = 'Obrigatório.';
        if (!form.equipmentType) newErrors.equipmentType = 'Obrigatório.';

        // Validate purchase traceability fields
        if (!purchase.purchaseInfoUnavailable) {
            if (!purchase.purchaseAmount.toString().trim()) newErrors.purchaseAmount = 'Obrigatório.';
            if (!purchase.acquisitionDate.trim()) newErrors.acquisitionDate = 'Obrigatório.';
            if (!purchase.invoiceNumber.trim()) newErrors.invoiceNumber = 'Obrigatório.';
            if (!purchase.supplierId) newErrors.supplierId = 'Obrigatório.';

            // Require purchase document on create
            if (!isEdit && !purchaseDocFile) {
                newErrors.purchaseDocFile = 'Cópia da nota de compra é obrigatória.';
            }
            // On edit, check if purchase document already exists
            if (isEdit && equipment) {
                const hasPurchaseDoc = equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT');
                if (!hasPurchaseDoc && !purchaseDocFile) {
                    newErrors.purchaseDocFile = 'Cópia da nota de compra é obrigatória.';
                }
            }
        } else {
            if (!purchase.purchaseInfoUnavailableReason.trim()) {
                newErrors.purchaseInfoUnavailableReason = 'Informe o motivo.';
            }
        }

        // Validate warranty fields
        if (!warranty.warrantyInfoUnavailable && !purchase.purchaseInfoUnavailable) {
            if (!warranty.warrantyMonths.trim() && !warranty.warrantyEndDate.trim()) {
                newErrors.warrantyMonths = 'Informe a duração ou a data fim.';
                newErrors.warrantyEndDate = 'Informe a duração ou a data fim.';
            }
        }
        if (warranty.warrantyInfoUnavailable && !warranty.warrantyInfoUnavailableReason.trim()) {
            newErrors.warrantyInfoUnavailableReason = 'Informe o motivo.';
        }

        if (Object.keys(newErrors).length > 0) {
            setFieldErrors(newErrors);
            setError('Preencha os campos obrigatórios corretamente.');
            return;
        }

        // Build acquisition payload (always included — independent of SourceType)
        const acquisitionPayload = {
            purchaseAmount: purchase.purchaseInfoUnavailable ? null : (parseFloat(purchase.purchaseAmount.toString()) || null),
            currency: purchase.currency || 'AOA',
            acquisitionDate: purchase.purchaseInfoUnavailable ? null : (purchase.acquisitionDate || null),
            invoiceNumber: purchase.purchaseInfoUnavailable ? null : (purchase.invoiceNumber || null),
            supplierId: purchase.purchaseInfoUnavailable ? null : purchase.supplierId,
            supplierName: purchase.purchaseInfoUnavailable ? null : (purchase.supplierName || null),
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

        try {
            setSaving(true);
            setError('');

            if (isEdit && equipment) {
                const updatePayload: any = {
                    hostname: form.hostname,
                    plant: form.plant,
                    equipmentType: form.equipmentType,
                    manufacturer: form.manufacturer,
                    model: form.model,
                    serialNumber: form.serialNumber,
                    macAddress: form.macAddress,
                    wifiMacAddress: form.wifiMacAddress,
                    processor: form.processor,
                    memoryRam: form.memoryRam,
                    color: form.color,
                    biometricMfaEnabled: form.biometricMfaEnabled,
                    idCard: form.idCard,
                    notes: form.notes,
                    legacyAssetCode: form.legacyAssetCode || null,
                    manufactureDate: form.manufactureDate || null,
                    acquisition: acquisitionPayload,
                };
                await itEquipmentApi.update(equipment.id, updatePayload);
            } else {
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

                // Upload purchase document if file was selected
                if (purchaseDocFile && result.id) {
                    try {
                        await itEquipmentApi.documents.upload(result.id, purchaseDocFile, 'PURCHASE_DOCUMENT');
                    } catch (uploadErr) {
                        // Equipment created but document upload failed — show warning, don't delete equipment
                        setPurchaseDocError(
                            `Equipamento criado (${result.assetTag}), mas o documento de compra não foi carregado. ` +
                            `O equipamento ficará com cadastro incompleto até que o documento seja carregado. ` +
                            `Abra o equipamento para tentar novamente.`
                        );
                        setSaving(false);
                        // Still refresh the list to show the new equipment
                        onSuccess();
                        return;
                    }
                }
            }

            // Upload purchase document on edit (if a new file was selected)
            if (isEdit && equipment && purchaseDocFile) {
                try {
                    await itEquipmentApi.documents.upload(equipment.id, purchaseDocFile, 'PURCHASE_DOCUMENT');
                } catch (uploadErr) {
                    setPurchaseDocError('O documento de compra não foi carregado. Tente novamente.');
                }
            }

            onSuccess();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao salvar.';
            setError(message);
        } finally {
            setSaving(false);
        }
    };

    // Type-sensitive field visibility
    const computeTypes = ['LAPTOP', 'DESKTOP', 'SERVER', 'TABLET'];
    const networkTypes = ['SWITCH', 'FIREWALL', 'ACCESS_POINT', 'NETWORK_EQUIPMENT', 'NVR'];
    const showHostname = [...computeTypes, ...networkTypes].includes(form.equipmentType);
    const showProcessorRam = computeTypes.includes(form.equipmentType);
    const showMacAddress = [...computeTypes, ...networkTypes, 'PRINTER'].includes(form.equipmentType);

    // Build manufacturer options with backward compat
    const manufacturerOpts = buildOptionsWithLegacy(
        manufacturers.map(m => ({ value: m.name, label: m.name })),
        form.manufacturer,
        'Selecione...'
    );

    // Build model options with backward compat
    const modelOpts = buildOptionsWithLegacy(
        models.map(m => ({ value: m.name, label: m.name })),
        form.model,
        form.manufacturer ? 'Selecione...' : 'Selecione fabricante primeiro'
    );

    // Build processor options with backward compat
    const processorOpts = buildOptionsWithLegacy(
        processors.map(p => ({ value: p.name, label: p.name })),
        form.processor,
        'Selecione...'
    );

    // Build memory options with backward compat
    const memoryOpts = buildOptionsWithLegacy(
        memoryOptions.map(m => ({ value: m.displayName, label: m.displayName })),
        form.memoryRam,
        'Selecione...'
    );

    return (
        <ModalWrapper title={isEdit ? 'Editar Equipamento' : 'Novo Equipamento'} onClose={onClose} wide>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}

                <div style={{ display: 'flex', gap: 12 }}>
                    {isEdit && equipment ? (
                        <div style={{ flex: 1 }}>
                            <label style={labelStyle}>Código do Ativo</label>
                            <div style={{ padding: '8px 12px', background: '#f0fdf4', border: '1px solid #86efac', borderRadius: 6, fontWeight: 600, fontFamily: 'monospace', fontSize: 14, color: '#166534', letterSpacing: '0.5px' }}>
                                {equipment.assetTag}
                            </div>
                        </div>
                    ) : (
                        <div style={{ flex: 1 }}>
                            <label style={labelStyle}>Código do Ativo</label>
                            <div style={{ padding: '8px 12px', background: '#f8fafc', border: '1px dashed #94a3b8', borderRadius: 6, color: '#64748b', fontSize: 13, fontStyle: 'italic' }}>
                                Gerado automaticamente ao salvar
                            </div>
                        </div>
                    )}
                    {showHostname && <FormInput label="Hostname" value={form.hostname} onChange={v => set('hostname', v)} />}
                </div>
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormSearchableSelect label="Tipo" value={form.equipmentType} onChange={handleTypeChange}
                        options={equipmentTypes.length > 0 ? equipmentTypes : [{ value: 'UNKNOWN', label: 'Carregando...' }]}
                        error={fieldErrors.equipmentType}
                        required
                    />
                    {!isEdit && (
                        <FormSelect label="Status" value={form.statusCode} onChange={v => set('statusCode', v)}
                            options={[
                                { value: 'AVAILABLE', label: 'Disponível' },
                                { value: 'IN_USE', label: 'Em uso' },
                                { value: 'IN_REPAIR', label: 'Em conserto' },
                                { value: 'RESERVED', label: 'Reservado' },
                            ]}
                        />
                    )}
                </div>

                {/* Company → Plant cascade */}
                {!isEdit ? (
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormSelect label="Empresa *" value={form.companyId} onChange={handleCompanyChange}
                            options={[
                                { value: '', label: 'Selecione...' },
                                ...companies.filter((c) => c.isActive).map((c) => ({ value: String(c.id), label: c.name }))
                            ]}
                            error={fieldErrors.companyId}
                        />
                        <FormSearchableSelect label="Planta" value={form.plantId} onChange={v => { set('plantId', v); const pl = plants.find(p => String(p.id) === v); if (pl) set('plant', pl.name); }}
                            options={[
                                { value: '', label: form.companyId ? 'Selecione...' : 'Selecione empresa primeiro' },
                                ...plants.filter((p) => p.isActive).map((p) => ({ value: String(p.id), label: p.name }))
                            ]}
                            disabled={!form.companyId}
                            error={fieldErrors.plantId}
                            required
                        />
                    </div>
                ) : (
                    <div style={{ display: 'flex', gap: 12 }}>
                        <FormInput label="Planta" value={form.plant} onChange={v => set('plant', v)} />
                        <FormInput label="Código Legado" value={form.legacyAssetCode} onChange={v => set('legacyAssetCode', v)} placeholder="Código patrimônio antigo (opcional)" />
                    </div>
                )}

                {/* Manufacturer → Model cascade */}
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormSearchableSelect label="Fabricante" value={form.manufacturer} onChange={handleManufacturerChange}
                        options={manufacturerOpts}
                    />
                    <FormSearchableSelect label="Modelo" value={form.model} onChange={v => set('model', v)}
                        options={modelOpts}
                        disabled={!form.manufacturer && !isEdit}
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
                        <FormSearchableSelect label="Processador" value={form.processor} onChange={v => set('processor', v)}
                            options={processorOpts}
                        />
                        <FormSearchableSelect label="RAM" value={form.memoryRam} onChange={v => set('memoryRam', v)}
                            options={memoryOpts}
                        />
                    </div>
                )}
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormInput label="Cor" value={form.color} onChange={v => set('color', v)} />
                    <FormInput label="ID Card" value={form.idCard} onChange={v => set('idCard', v)} />
                </div>
                <div style={{ display: 'flex', gap: 12 }}>
                    <FormInput label="Data de Fabricação" type="date" value={form.manufactureDate} onChange={v => set('manufactureDate', v)} style={{ flex: 1 }} />
                    <div style={{ flex: 1, paddingTop: 18 }}><FormCheckbox label="Biometria / MFA" checked={form.biometricMfaEnabled} onChange={v => set('biometricMfaEnabled', v)} id="biocheck" /></div>
                </div>

                {/* Source Type (create only) */}
                {!isEdit && (
                    <FormSelect label="Origem do Equipamento" value={form.sourceType} onChange={v => set('sourceType', v)}
                        options={[
                            { value: 'MANUAL_REGISTRATION', label: 'Registo Manual' },
                            { value: 'MANUAL_PURCHASE', label: 'Compra / Aquisição' },
                        ]}
                    />
                )}

                {/* ── Purchase / Traceability — always visible ── */}
                <SectionCard title="Compra / Rastreabilidade" icon={<span style={{fontSize: '1rem'}}>📋</span>}>

                    {/* Unavailable toggle */}
                    <FormCheckbox label="Informações de compra indisponíveis" checked={purchase.purchaseInfoUnavailable} onChange={v => setPur('purchaseInfoUnavailable', v)} id="purchaseUnavailableCheck" style={{ marginBottom: 12 }} />

                    {purchase.purchaseInfoUnavailable ? (
                        /* Reason for unavailability */
                        <div>
                            <label style={labelStyle}>Motivo da indisponibilidade *</label>
                            <textarea
                                value={purchase.purchaseInfoUnavailableReason}
                                onChange={e => setPur('purchaseInfoUnavailableReason', e.target.value)}
                                rows={2}
                                placeholder="Ex: Equipamento adquirido antes da implementação do sistema de rastreabilidade."
                                style={{ ...inputStyle, resize: 'vertical', borderColor: fieldErrors.purchaseInfoUnavailableReason ? '#ef4444' : 'var(--color-border)' }}
                            />
                            {fieldErrors.purchaseInfoUnavailableReason && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.purchaseInfoUnavailableReason}</div>}
                        </div>
                    ) : (
                        /* Purchase data fields */
                        <>
                            <div style={{ display: 'flex', gap: 12 }}>
                                <FormInput label="Valor de compra *" value={purchase.purchaseAmount.toString()} onChange={v => setPur('purchaseAmount', v)} placeholder="0.00" error={fieldErrors.purchaseAmount} />
                                <FormSelect label="Moeda" value={purchase.currency} onChange={v => setPur('currency', v)}
                                    options={[
                                        { value: 'AOA', label: 'AOA — Kwanza' },
                                        { value: 'USD', label: 'USD — Dólar' },
                                        { value: 'EUR', label: 'EUR — Euro' },
                                    ]}
                                />
                            </div>
                            <div style={{ display: 'flex', gap: 12 }}>
                                <FormInput label="Data de compra *" type="date" value={purchase.acquisitionDate} onChange={v => setPur('acquisitionDate', v)} error={fieldErrors.acquisitionDate} style={{ flex: 1 }} />
                                <FormInput label="Nº do documento de compra / entrega *" value={purchase.invoiceNumber} onChange={v => setPur('invoiceNumber', v)} placeholder="Fatura, guia, ou documento interno" error={fieldErrors.invoiceNumber} />
                            </div>
                            <div style={{ display: 'flex', gap: 12 }}>
                                <div style={{ flex: 1 }}>
                                    <label style={labelStyle}>Fornecedor *</label>
                                    <div style={{ border: fieldErrors.supplierId ? '1px solid #ef4444' : 'none', borderRadius: 6 }}>
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
                                    {fieldErrors.supplierId && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.supplierId}</div>}
                                </div>
                                <FormInput label="Nº Ordem de Compra" value={purchase.purchaseOrderNumber} onChange={v => setPur('purchaseOrderNumber', v)} />
                            </div>

                            {/* Purchase document upload */}
                            <div style={{ marginTop: 8 }}>
                                <FileUpload
                                    label="Cópia da nota de compra / guia de entrega *"
                                    file={purchaseDocFile}
                                    existingFileName={isEdit && equipment && equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT') ? "Documento existente" : undefined}
                                    onChange={(file) => {
                                        setPurchaseDocError('');
                                        setPurchaseDocFile(file);
                                    }}
                                    onRemoveExisting={() => {
                                        // The user is replacing or removing it.
                                    }}
                                    accept=".pdf,.jpg,.jpeg,.png"
                                    maxSizeMB={10}
                                    error={purchaseDocError || fieldErrors.purchaseDocFile}
                                    helperText="PDF, JPG ou PNG — máximo 10 MB"
                                />
                                {isEdit && equipment && equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT') && !purchaseDocFile && (
                                    <div style={{
                                        padding: '6px 10px', backgroundColor: '#ecfdf5', border: '1px solid #a7f3d0',
                                        borderRadius: 6, fontSize: '0.8rem', color: '#059669', marginTop: 8
                                    }}>
                                        ✅ Documento já carregado — carregue um novo arquivo acima para substituir.
                                    </div>
                                )}
                            </div>
                        </>
                    )}
                </SectionCard>

                {/* ── Warranty Section ── */}
                <SectionCard title="Garantia" icon={<span style={{fontSize: '1rem'}}>🛡️</span>}>

                    <FormCheckbox label="Informações de garantia indisponíveis" checked={warranty.warrantyInfoUnavailable} onChange={v => setWar('warrantyInfoUnavailable', v)} id="warrantyUnavailableCheck" style={{ marginBottom: 12 }} />

                    {warranty.warrantyInfoUnavailable ? (
                        <div>
                            <label style={labelStyle}>Motivo da indisponibilidade *</label>
                            <textarea
                                value={warranty.warrantyInfoUnavailableReason}
                                onChange={e => setWar('warrantyInfoUnavailableReason', e.target.value)}
                                rows={2}
                                placeholder="Ex: Informações de garantia não disponíveis — equipamento recebido sem documentação."
                                style={{ ...inputStyle, resize: 'vertical', borderColor: fieldErrors.warrantyInfoUnavailableReason ? '#ef4444' : 'var(--color-border)' }}
                            />
                            {fieldErrors.warrantyInfoUnavailableReason && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.warrantyInfoUnavailableReason}</div>}
                        </div>
                    ) : (
                        <>
                            <div style={{ display: 'flex', gap: 12 }}>
                                <FormInput label="Garantia (meses)" value={warranty.warrantyMonths}
                                    onChange={v => setWar('warrantyMonths', v)} type="number" placeholder="12" error={fieldErrors.warrantyMonths} />
                                <FormInput label="Início da garantia" type="date" value={warranty.warrantyStartDate} onChange={v => setWar('warrantyStartDate', v)} style={{ flex: 1 }} />
                            </div>
                            <div style={{ display: 'flex', gap: 12 }}>
                                <FormInput label="Fim da garantia" type="date" value={warranty.warrantyEndDate} onChange={v => setWar('warrantyEndDate', v)} error={fieldErrors.warrantyEndDate} helperText={warranty.warrantyMonths && warranty.warrantyEndDate ? `Calculado automaticamente a partir de ${warranty.warrantyMonths} meses. Editável.` : undefined} style={{ flex: 1 }} />
                                <FormInput label="Notas de garantia" value={warranty.warrantyNotes}
                                    onChange={v => setWar('warrantyNotes', v)} placeholder="Informações adicionais" />
                            </div>
                        </>
                    )}
                </SectionCard>

                <div style={{ marginTop: 16 }}>
                    <FormTextarea label="Notas" value={form.notes} onChange={v => set('notes', v)} rows={3} />
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 16, paddingTop: 16, borderTop: '1px solid var(--color-border)' }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label={isEdit ? 'Salvar' : 'Criar Equipamento'} loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}

// ── Helper: build options list with backward compat for legacy free-text values ──
function buildOptionsWithLegacy(
    options: Array<{ value: string; label: string }>,
    currentValue: string,
    placeholder: string
): Array<{ value: string; label: string }> {
    const opts: Array<{ value: string; label: string }> = [{ value: '', label: placeholder }];
    // If current value exists and isn't in the options list, prepend it
    if (currentValue && !options.some(o => o.value === currentValue)) {
        opts.push({ value: currentValue, label: `${currentValue} (valor existente)` });
    }
    opts.push(...options);
    return opts;
}

// ─── Shared modal helpers (exported for other modals) ───

export { ModalWrapper } from '../common/ModalWrapper';

export function SubmitBtn({ label, loading, disabled }: { label: string; loading: boolean; disabled?: boolean }) {
    const isDisabled = loading || disabled;
    return (
        <button type="submit" disabled={isDisabled} style={{
            display: 'flex', alignItems: 'center', gap: 6, padding: '8px 20px',
            background: isDisabled ? '#94a3b8' : 'linear-gradient(135deg, #3b82f6, #2563eb)', border: 'none',
            borderRadius: 8, color: '#fff', fontSize: '0.85rem', fontWeight: 600,
            cursor: isDisabled ? 'default' : 'pointer', opacity: isDisabled ? 0.6 : 1
        }}>
            {loading && <Loader2 size={14} style={{ animation: 'spin 1s linear infinite' }} />}
            {label}
        </button>
    );
}

export function ErrorBox({ msg }: { msg: string }) {
    return (
        <div style={{
            padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca',
            borderRadius: 8, color: '#dc2626', fontSize: '0.82rem'
        }}>
            {msg}
        </div>
    );
}

export function Field({ label, value, onChange, disabled, type, placeholder, error }: {
    label: string; value: string; onChange: (v: string) => void; disabled?: boolean; type?: string; placeholder?: string; error?: string;
}) {
    return (
        <div style={{ flex: 1 }}>
            <label style={labelStyle}>{label}</label>
            <input type={type || 'text'} value={value} onChange={e => onChange(e.target.value)} disabled={disabled}
                placeholder={placeholder} style={{ ...inputStyle, opacity: disabled ? 0.6 : 1, borderColor: error ? '#ef4444' : 'var(--color-border)' }} />
            {error && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{error}</div>}
        </div>
    );
}

export function SelectField({ label, value, onChange, options, disabled, error }: {
    label: string; value: string; onChange: (v: string) => void; options: Array<{ value: string; label: string }>; disabled?: boolean; error?: string;
}) {
    return (
        <div style={{ flex: 1 }}>
            <label style={labelStyle}>{label}</label>
            <select value={value} onChange={e => onChange(e.target.value)} style={{ ...inputStyle, opacity: disabled ? 0.5 : 1, borderColor: error ? '#ef4444' : 'var(--color-border)' }} disabled={disabled}>
                {options.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
            </select>
            {error && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{error}</div>}
        </div>
    );
}

export function Row({ children }: { children: React.ReactNode }) {
    return <div style={{ display: 'flex', gap: 12 }}>{children}</div>;
}

export function TextArea({ label, value, onChange, rows, error }: {
    label: string; value: string; onChange: (v: string) => void; rows?: number; error?: string;
}) {
    return (
        <div>
            <label style={labelStyle}>{label}</label>
            <textarea value={value} onChange={e => onChange(e.target.value)} rows={rows || 3}
                style={{ ...inputStyle, resize: 'vertical', borderColor: error ? '#ef4444' : 'var(--color-border)' }} />
            {error && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{error}</div>}
        </div>
    );
}

export const labelStyle: React.CSSProperties = {
    display: 'block', fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)',
    marginBottom: 4
};

export const inputStyle: React.CSSProperties = {
    width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
    borderRadius: 6, backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text)',
    fontSize: '0.85rem', outline: 'none', boxSizing: 'border-box'
};

export const cancelBtnStyle: React.CSSProperties = {
    padding: '8px 16px', border: '1px solid var(--color-border)', borderRadius: 8,
    background: 'transparent', color: 'var(--color-text-muted)', cursor: 'pointer',
    fontSize: '0.85rem', fontWeight: 500
};
