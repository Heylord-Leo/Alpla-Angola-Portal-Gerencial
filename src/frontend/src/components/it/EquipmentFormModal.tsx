import React, { useState, useEffect } from 'react';
import { X, Loader2 } from 'lucide-react';
import { itEquipmentApi, itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import type { ITEquipmentDetail } from '../../types/itEquipment';

interface Props {
    equipment?: ITEquipmentDetail;
    onClose: () => void;
    onSuccess: () => void;
}

export function EquipmentFormModal({ equipment, onClose, onSuccess }: Props) {
    const isEdit = !!equipment;
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);

    // ── Master Data lookups ──
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [manufacturers, setManufacturers] = useState<any[]>([]);
    const [models, setModels] = useState<any[]>([]);
    const [processors, setProcessors] = useState<any[]>([]);
    const [memoryOptions, setMemoryOptions] = useState<any[]>([]);

    // Load dynamic equipment types
    useEffect(() => {
        itEquipmentApi.types.list(true).then(types => {
            setEquipmentTypes(types.map((t: any) => ({ value: t.code, label: t.displayName })));
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
        api.lookups.getCompanies().then(setCompanies).catch(() => {});
        itEquipmentCatalogApi.manufacturers.list(true).then(setManufacturers).catch(() => {});
        itEquipmentCatalogApi.processors.list(true).then(setProcessors).catch(() => {});
        itEquipmentCatalogApi.memoryOptions.list(true).then(setMemoryOptions).catch(() => {});
    }, []);

    const [form, setForm] = useState({
        assetTag: equipment?.assetTag || '',
        hostname: equipment?.hostname || '',
        companyId: '',
        plant: equipment?.plant || '',
        equipmentType: equipment?.equipmentType || 'LAPTOP',
        statusCode: equipment?.statusCode || 'AVAILABLE',
        manufacturer: equipment?.manufacturer || '',
        model: equipment?.model || '',
        serialNumber: equipment?.serialNumber || '',
        macAddress: equipment?.macAddress || '',
        processor: equipment?.processor || '',
        memoryRam: equipment?.memoryRam || '',
        color: equipment?.color || '',
        biometricMfaEnabled: equipment?.biometricMfaEnabled || false,
        idCard: equipment?.idCard || '',
        notes: equipment?.notes || '',
        sourceType: equipment?.sourceType || 'MANUAL_REGISTRATION',
    });

    // Purchase tracking state (only for creation with MANUAL_PURCHASE)
    const [purchase, setPurchase] = useState({
        purchaseAmount: equipment?.acquisition?.purchaseAmount?.toString() || '',
        currency: equipment?.acquisition?.currency || 'AOA',
        acquisitionDate: equipment?.acquisition?.acquisitionDate ? equipment.acquisition.acquisitionDate.split('T')[0] : '',
        supplierName: equipment?.acquisition?.supplierName || '',
        purchaseOrderNumber: equipment?.acquisition?.purchaseOrderNumber || '',
        invoiceNumber: equipment?.acquisition?.invoiceNumber || '',
    });

    const set = (field: string, value: any) => setForm(prev => ({ ...prev, [field]: value }));
    const setPur = (field: string, value: string) => setPurchase(prev => ({ ...prev, [field]: value }));

    const showPurchase = form.sourceType === 'MANUAL_PURCHASE';

    // ── Company → Plant cascade ──
    useEffect(() => {
        if (form.companyId) {
            api.lookups.getPlants(Number(form.companyId)).then(setPlants).catch(() => setPlants([]));
        } else {
            setPlants([]);
        }
    }, [form.companyId]);

    const handleCompanyChange = (v: string) => {
        set('companyId', v);
        set('plant', ''); // Clear plant when company changes
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
        if (!form.assetTag.trim()) { setError('Asset Tag é obrigatório.'); return; }
        if (!isEdit && !form.companyId) { setError('Empresa é obrigatória.'); return; }
        if (!isEdit && !form.plant) { setError('Planta é obrigatória.'); return; }

        // Validate purchase fields
        if (!isEdit && showPurchase) {
            if (!purchase.purchaseAmount.trim()) { setError('Valor de compra é obrigatório para equipamento de compra.'); return; }
        }

        try {
            setSaving(true);
            setError('');

            const payload: any = { ...form };
            delete payload.companyId; // Not sent to backend (Plant text is enough for ITEquipment)

            // Attach acquisition data for MANUAL_PURCHASE
            if (!isEdit && showPurchase) {
                payload.acquisition = {
                    purchaseAmount: parseFloat(purchase.purchaseAmount) || 0,
                    currency: purchase.currency || 'AOA',
                    acquisitionDate: purchase.acquisitionDate || null,
                    supplierName: purchase.supplierName || null,
                    purchaseOrderNumber: purchase.purchaseOrderNumber || null,
                    invoiceNumber: purchase.invoiceNumber || null,
                };
            }

            if (isEdit && equipment) {
                await itEquipmentApi.update(equipment.id, payload);
            } else {
                await itEquipmentApi.create(payload);
            }
            onSuccess();
        } catch (err: any) {
            setError(err.message || 'Erro ao salvar.');
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

                <Row>
                    <Field label="Asset Tag *" value={form.assetTag} onChange={v => set('assetTag', v)} disabled={isEdit} />
                    {showHostname && <Field label="Hostname" value={form.hostname} onChange={v => set('hostname', v)} />}
                </Row>
                <Row>
                    <SelectField label="Tipo *" value={form.equipmentType} onChange={handleTypeChange}
                        options={equipmentTypes.length > 0 ? equipmentTypes : [{ value: 'UNKNOWN', label: 'Carregando...' }]}
                    />
                    {!isEdit && (
                        <SelectField label="Status" value={form.statusCode} onChange={v => set('statusCode', v)}
                            options={[
                                { value: 'AVAILABLE', label: 'Disponível' },
                                { value: 'IN_USE', label: 'Em uso' },
                                { value: 'IN_REPAIR', label: 'Em conserto' },
                                { value: 'RESERVED', label: 'Reservado' },
                            ]}
                        />
                    )}
                </Row>

                {/* Company → Plant cascade */}
                {!isEdit ? (
                    <Row>
                        <SelectField label="Empresa *" value={form.companyId} onChange={handleCompanyChange}
                            options={[
                                { value: '', label: 'Selecione...' },
                                ...companies.filter((c: any) => c.isActive).map((c: any) => ({ value: String(c.id), label: c.name }))
                            ]}
                        />
                        <SelectField label="Planta *" value={form.plant} onChange={v => set('plant', v)}
                            options={[
                                { value: '', label: form.companyId ? 'Selecione...' : 'Selecione empresa primeiro' },
                                ...plants.filter((p: any) => p.isActive).map((p: any) => ({ value: p.name, label: p.name }))
                            ]}
                            disabled={!form.companyId}
                        />
                    </Row>
                ) : (
                    <Row>
                        <Field label="Planta" value={form.plant} onChange={v => set('plant', v)} />
                    </Row>
                )}

                {/* Manufacturer → Model cascade */}
                <Row>
                    <SelectField label="Fabricante" value={form.manufacturer} onChange={handleManufacturerChange}
                        options={manufacturerOpts}
                    />
                    <SelectField label="Modelo" value={form.model} onChange={v => set('model', v)}
                        options={modelOpts}
                        disabled={!form.manufacturer && !isEdit}
                    />
                </Row>

                <Row>
                    <Field label="Serial Number" value={form.serialNumber} onChange={v => set('serialNumber', v)} />
                    {showMacAddress && <Field label="MAC Address" value={form.macAddress} onChange={v => set('macAddress', v)} />}
                </Row>
                {showProcessorRam && (
                    <Row>
                        <SelectField label="Processador" value={form.processor} onChange={v => set('processor', v)}
                            options={processorOpts}
                        />
                        <SelectField label="RAM" value={form.memoryRam} onChange={v => set('memoryRam', v)}
                            options={memoryOpts}
                        />
                    </Row>
                )}
                <Row>
                    <Field label="Cor" value={form.color} onChange={v => set('color', v)} />
                    <Field label="ID Card" value={form.idCard} onChange={v => set('idCard', v)} />
                </Row>
                <Row>
                    <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 8 }}>
                        <input type="checkbox" checked={form.biometricMfaEnabled} onChange={e => set('biometricMfaEnabled', e.target.checked)} id="biocheck" />
                        <label htmlFor="biocheck" style={{ fontSize: '0.85rem', color: 'var(--color-text)' }}>Biometria / MFA</label>
                    </div>
                </Row>

                {/* Source Type / Acquisition */}
                {!isEdit && (
                    <>
                        <SelectField label="Origem do Equipamento" value={form.sourceType} onChange={v => set('sourceType', v)}
                            options={[
                                { value: 'MANUAL_REGISTRATION', label: 'Registo Manual' },
                                { value: 'MANUAL_PURCHASE', label: 'Compra / Aquisição' },
                            ]}
                        />
                        {showPurchase && (
                            <div style={{
                                border: '1px solid var(--color-border)', borderRadius: 10, padding: 14,
                                background: 'var(--color-bg-surface)', marginTop: 4
                            }}>
                                <div style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', marginBottom: 10 }}>
                                    💰 Dados de Compra
                                </div>
                                <Row>
                                    <Field label="Valor de Compra *" value={purchase.purchaseAmount} onChange={v => setPur('purchaseAmount', v)} />
                                    <SelectField label="Moeda" value={purchase.currency} onChange={v => setPur('currency', v)}
                                        options={[
                                            { value: 'AOA', label: 'AOA — Kwanza' },
                                            { value: 'USD', label: 'USD — Dólar' },
                                            { value: 'EUR', label: 'EUR — Euro' },
                                        ]}
                                    />
                                </Row>
                                <Row>
                                    <div style={{ flex: 1 }}>
                                        <label style={labelStyle}>Data de Aquisição</label>
                                        <input type="date" value={purchase.acquisitionDate} onChange={e => setPur('acquisitionDate', e.target.value)} style={inputStyle} />
                                    </div>
                                    <Field label="Fornecedor" value={purchase.supplierName} onChange={v => setPur('supplierName', v)} />
                                </Row>
                                <Row>
                                    <Field label="Nº Ordem de Compra" value={purchase.purchaseOrderNumber} onChange={v => setPur('purchaseOrderNumber', v)} />
                                    <Field label="Nº Fatura" value={purchase.invoiceNumber} onChange={v => setPur('invoiceNumber', v)} />
                                </Row>
                            </div>
                        )}
                    </>
                )}

                <div>
                    <label style={{ ...labelStyle }}>Notas</label>
                    <textarea value={form.notes} onChange={e => set('notes', e.target.value)} rows={3}
                        style={{ ...inputStyle, resize: 'vertical' }} />
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
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

export function ModalWrapper({ title, onClose, children, wide, width }: { title: string; onClose: () => void; children: React.ReactNode; wide?: boolean; width?: number }) {
    return (
        <>
            <div onClick={onClose} style={{
                position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', zIndex: 2000
            }} />
            <div style={{
                position: 'fixed', top: '50%', left: '50%', transform: 'translate(-50%, -50%)',
                width: width ?? (wide ? 640 : 480), maxHeight: '85vh', backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)', borderRadius: 14,
                boxShadow: '0 20px 60px rgba(0,0,0,0.2)', zIndex: 2001,
                display: 'flex', flexDirection: 'column',
                animation: 'modalIn 0.2s ease-out'
            }}>
                <style>{`@keyframes modalIn { from { opacity: 0; transform: translate(-50%, -48%); } to { opacity: 1; transform: translate(-50%, -50%); } }`}</style>
                <div style={{
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    padding: '14px 20px', borderBottom: '1px solid var(--color-border)'
                }}>
                    <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: 'var(--color-text)' }}>{title}</h3>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' }}>
                        <X size={18} />
                    </button>
                </div>
                <div style={{ padding: '16px 20px', overflowY: 'auto', flex: 1 }}>
                    {children}
                </div>
            </div>
        </>
    );
}

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

export function Field({ label, value, onChange, disabled, type, placeholder }: {
    label: string; value: string; onChange: (v: string) => void; disabled?: boolean; type?: string; placeholder?: string;
}) {
    return (
        <div style={{ flex: 1 }}>
            <label style={labelStyle}>{label}</label>
            <input type={type || 'text'} value={value} onChange={e => onChange(e.target.value)} disabled={disabled}
                placeholder={placeholder} style={{ ...inputStyle, opacity: disabled ? 0.6 : 1 }} />
        </div>
    );
}

export function SelectField({ label, value, onChange, options, disabled }: {
    label: string; value: string; onChange: (v: string) => void; options: Array<{ value: string; label: string }>; disabled?: boolean;
}) {
    return (
        <div style={{ flex: 1 }}>
            <label style={labelStyle}>{label}</label>
            <select value={value} onChange={e => onChange(e.target.value)} style={{ ...inputStyle, opacity: disabled ? 0.5 : 1 }} disabled={disabled}>
                {options.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
            </select>
        </div>
    );
}

export function Row({ children }: { children: React.ReactNode }) {
    return <div style={{ display: 'flex', gap: 12 }}>{children}</div>;
}

export function TextArea({ label, value, onChange, rows }: {
    label: string; value: string; onChange: (v: string) => void; rows?: number;
}) {
    return (
        <div>
            <label style={labelStyle}>{label}</label>
            <textarea value={value} onChange={e => onChange(e.target.value)} rows={rows || 3}
                style={{ ...inputStyle, resize: 'vertical' }} />
        </div>
    );
}

export const labelStyle: React.CSSProperties = {
    display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)',
    textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 4
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
