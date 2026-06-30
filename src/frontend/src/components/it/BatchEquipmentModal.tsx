import React, { useState, useEffect } from 'react';
import { itEquipmentApi, itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import type { ITEquipmentTypeItem, MasterDataCompany, MasterDataPlant, CatalogManufacturer, CatalogModel } from '../../types/itEquipment';
import { ModalWrapper, SubmitBtn, ErrorBox, cancelBtnStyle, labelStyle, inputStyle } from './EquipmentFormModal';
import { SupplierAutocomplete } from '../SupplierAutocomplete';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { FormInput } from '../common/form/FormInput';
import { FormSelect } from '../common/form/FormSelect';
import { FormTextarea } from '../common/form/FormTextarea';
import { FormCheckbox } from '../common/form/FormCheckbox';
import { FileUpload } from '../common/form/FileUpload';
import { SectionCard } from '../common/ui/SectionCard';

interface Props {
    onClose: () => void;
    onSuccess: () => void;
}

export function BatchEquipmentModal({ onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);

    // ── Master Data lookups ──
    const [companies, setCompanies] = useState<MasterDataCompany[]>([]);
    const [plants, setPlants] = useState<MasterDataPlant[]>([]);
    const [manufacturers, setManufacturers] = useState<CatalogManufacturer[]>([]);
    const [models, setModels] = useState<CatalogModel[]>([]);

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

    useEffect(() => {
        api.lookups.getCompanies().then(setCompanies).catch(() => {});
        itEquipmentCatalogApi.manufacturers.list(true).then(setManufacturers).catch(() => {});
    }, []);

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

    // Dynamic items
    const [items, setItems] = useState<Array<{ id: number; serialNumber: string; hostname: string; macAddress: string; wifiMacAddress: string; idCard: string; notes: string }>>([
        { id: Date.now(), serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' },
        { id: Date.now() + 1, serialNumber: '', hostname: '', macAddress: '', wifiMacAddress: '', idCard: '', notes: '' }
    ]);

    const [pendingQuantity, setPendingQuantity] = useState<string | null>(null);
    const [isConfirmingBatch, setIsConfirmingBatch] = useState(false);

    const set = (field: string, value: any) => setForm(prev => ({ ...prev, [field]: value }));
    const setPur = (field: string, value: any) => setPurchase(prev => ({ ...prev, [field]: value }));
    const setWar = (field: string, value: any) => setWarranty(prev => ({ ...prev, [field]: value }));

    const handleQuantityChange = (newQtyStr: string) => {
        const newQty = parseInt(newQtyStr);
        if (isNaN(newQty)) {
            set('quantity', newQtyStr);
            return;
        }
        const currentQty = parseInt(form.quantity) || 2;
        if (newQty < currentQty) {
            const itemsToRemove = items.slice(newQty);
            const hasData = itemsToRemove.some(it => it.serialNumber || it.hostname || it.macAddress || it.idCard);
            if (hasData) {
                setPendingQuantity(newQtyStr);
                return;
            }
        }
        set('quantity', newQtyStr);
    };

    const confirmQuantityReduction = () => {
        if (pendingQuantity !== null) {
            set('quantity', pendingQuantity);
            setPendingQuantity(null);
        }
    };

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

    useEffect(() => {
        if (!warranty.warrantyStartDate && purchase.acquisitionDate) {
            setWar('warrantyStartDate', purchase.acquisitionDate);
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
        set('plantId', '');
    };

    useEffect(() => {
        if (form.manufacturer) {
            const mfr = manufacturers.find(m => m.name === form.manufacturer);
            if (mfr) {
                itEquipmentCatalogApi.models.list({ activeOnly: true, manufacturerId: mfr.id, equipmentTypeCode: form.equipmentType || undefined }).then(setModels).catch(() => setModels([]));
            } else setModels([]);
        } else setModels([]);
    }, [form.manufacturer, form.equipmentType, manufacturers]);

    const handleManufacturerChange = (v: string) => {
        set('manufacturer', v);
        set('model', '');
    };

    const handleTypeChange = (v: string) => {
        set('equipmentType', v);
        set('model', '');
    };

    const updateItem = (index: number, field: string, value: string) => {
        const newItems = [...items];
        newItems[index] = { ...newItems[index], [field]: value };
        setItems(newItems);
    };

    const validateForm = () => {
        const qty = parseInt(form.quantity);
        if (isNaN(qty) || qty < 2 || qty > 100) { setError('O lote deve conter no mínimo 2 itens (máx. 100). Para criar apenas 1 equipamento, utilize o cadastro individual.'); return false; }
        if (!form.companyId) { setError('Empresa é obrigatória.'); return false; }
        if (!form.plantId) { setError('Planta é obrigatória.'); return false; }
        if (!form.equipmentType) { setError('Tipo de equipamento é obrigatório.'); return false; }

        if (!purchase.purchaseInfoUnavailable) {
            if (!purchase.purchaseAmount.toString().trim() || !purchase.acquisitionDate.trim() || !purchase.invoiceNumber.trim()) {
                setError('Informe o valor unitário de compra, data de compra e número do documento, ou marque as informações como indisponíveis.');
                return false;
            }
            if (!purchaseDocFile) {
                setError('Carregue a cópia da nota de compra / guia de entrega.');
                return false;
            }
        } else {
            if (!purchase.purchaseInfoUnavailableReason.trim()) {
                setError('Informe o motivo da indisponibilidade das informações de compra.');
                return false;
            }
        }

        if (!warranty.warrantyInfoUnavailable && !purchase.purchaseInfoUnavailable) {
            if (!warranty.warrantyMonths.trim() && !warranty.warrantyEndDate.trim()) {
                setError('Informe a garantia (meses) ou a data de fim, ou marque como indisponível.');
                return false;
            }
        } else if (warranty.warrantyInfoUnavailable) {
            if (!warranty.warrantyInfoUnavailableReason.trim()) {
                setError('Informe o motivo da indisponibilidade das informações de garantia.');
                return false;
            }
        }
        
        setError('');
        return true;
    };

    const handleInitialSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        if (validateForm()) {
            setIsConfirmingBatch(true);
        }
    };

    const confirmAndCreateBatch = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!validateForm()) return;
        
        setSaving(true);
        setError('');

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
            if (purchase.supplierId) {
                formData.append('supplierId', String(purchase.supplierId));
            }
            formData.append('supplierName', purchase.supplierName);
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

            if (purchaseDocFile) {
                formData.append('purchaseDocument', purchaseDocFile);
            }

            formData.append('itemsJson', JSON.stringify(items.map((it, i) => ({
                index: i + 1,
                serialNumber: it.serialNumber,
                hostname: it.hostname,
                macAddress: it.macAddress,
                wifiMacAddress: it.wifiMacAddress,
                idCard: it.idCard,
                notes: it.notes
            }))));

            await itEquipmentApi.createBatch(formData);
            onSuccess();
        } catch (err: any) {
            setError(err.message || 'Falha ao criar lote.');
            setSaving(false);
        }
    };

    if (pendingQuantity !== null) {
        return (
            <ConfirmationDialog
                title="Confirmar redução"
                message="Deseja reduzir a quantidade? Dados inseridos nas últimas linhas serão perdidos."
                confirmText="Confirmar redução"
                cancelText="Cancelar"
                onConfirm={confirmQuantityReduction}
                onCancel={() => setPendingQuantity(null)}
                variant="destructive"
            />
        );
    }

    if (isConfirmingBatch) {
        const qtyInt = parseInt(form.quantity) || 2;
        const pAmt = parseFloat(purchase.purchaseAmount);
        const totalAmount = (!isNaN(pAmt)) ? (pAmt * qtyInt).toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—';
        const typeLabel = equipmentTypes.find(t => t.value === form.equipmentType)?.label || form.equipmentType;

        return (
            <ModalWrapper title="Confirmar criação do lote" onClose={() => setIsConfirmingBatch(false)} width={600}>
                <form onSubmit={confirmAndCreateBatch} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <p style={{ fontSize: '15px', color: '#374151', margin: 0 }}>
                        Você está prestes a criar <strong>{form.quantity}</strong> equipamentos individuais.
                    </p>
                    
                    <div style={{ padding: '16px', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px' }}>
                        <h4 style={{ margin: '0 0 12px 0', fontSize: '14px', fontWeight: 600, color: '#0f172a' }}>Resumo do lote</h4>
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', fontSize: '13px' }}>
                            <div><strong style={{ color: '#64748b' }}>Quantidade:</strong> {form.quantity} equipamentos</div>
                            <div><strong style={{ color: '#64748b' }}>Tipo:</strong> {typeLabel}</div>
                            <div><strong style={{ color: '#64748b' }}>Fabricante:</strong> {form.manufacturer || '—'}</div>
                            <div><strong style={{ color: '#64748b' }}>Modelo:</strong> {form.model || '—'}</div>
                            <div><strong style={{ color: '#64748b' }}>Valor unitário:</strong> {purchase.purchaseAmount ? `${parseFloat(purchase.purchaseAmount).toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${purchase.currency}` : '—'}</div>
                            <div><strong style={{ color: '#64748b' }}>Total do lote:</strong> {totalAmount} {purchase.currency}</div>
                            <div style={{ gridColumn: '1 / -1' }}><strong style={{ color: '#64748b' }}>Fornecedor:</strong> {purchase.supplierName || '—'}</div>
                            <div style={{ gridColumn: '1 / -1' }}><strong style={{ color: '#64748b' }}>Documento:</strong> {purchase.invoiceNumber || '—'}</div>
                        </div>
                    </div>

                    <div style={{ fontSize: '13px', color: '#4b5563', padding: '12px', backgroundColor: '#f3f4f6', borderRadius: '6px' }}>
                        <ul style={{ margin: 0, paddingLeft: '20px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            <li>Cada equipamento receberá o seu próprio <strong>código de ativo</strong>.</li>
                            <li>Cada equipamento terá o seu próprio <strong>histórico</strong>.</li>
                            <li>Cada equipamento terá um <strong>ciclo de entrega/devolução independente</strong>.</li>
                            <li>A mesma nota/guia de compra será vinculada como referência para todos os itens.</li>
                        </ul>
                    </div>

                    {error && <ErrorBox msg={error} />}

                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px', paddingTop: '16px', borderTop: '1px solid #e5e7eb' }}>
                        <button type="button" onClick={() => setIsConfirmingBatch(false)} style={cancelBtnStyle} disabled={saving}>Cancelar</button>
                        <SubmitBtn label={`Confirmar criação de ${form.quantity} equipamentos`} loading={saving} disabled={saving} />
                    </div>
                </form>
            </ModalWrapper>
        );
    }

    return (
        <ModalWrapper title="Criar Lote de Equipamentos" onClose={onClose} wide width={1200}>
            <form onSubmit={handleInitialSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                
                <div style={{ padding: '16px', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px' }}>
                    <p style={{ margin: 0, fontSize: '14px', color: '#1e3a8a', fontWeight: 500, marginBottom: '12px' }}>
                        Use esta tela para cadastrar 2 ou mais equipamentos iguais. Para cadastrar apenas 1 equipamento, use "Novo Equipamento".
                    </p>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <label style={{ ...labelStyle, fontSize: '14px', fontWeight: 600, color: '#1e3a8a' }}>Quantidade de equipamentos *</label>
                        <input type="number" min="2" max="100" value={form.quantity} onChange={e => handleQuantityChange(e.target.value)} style={{ ...inputStyle, width: '100px', fontSize: '15px', fontWeight: 'bold' }} />
                    </div>
                </div>

                {error && <ErrorBox msg={error} />}

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px', paddingBottom: '20px', borderBottom: '1px solid #e5e7eb' }}>
                    {/* Left Col: Core Details */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                        <h3 style={{ fontSize: '15px', fontWeight: 600, color: '#111827', marginBottom: '4px' }}>Detalhes Comuns</h3>
                        <div style={{ display: 'flex', gap: 12 }}>
                            <FormSelect label="Empresa *" value={form.companyId} onChange={handleCompanyChange}
                                options={[{ value: '', label: 'Selecione...' }, ...companies.map(c => ({ value: String(c.id), label: c.name }))]} />
                            <FormSelect label="Planta *" value={form.plantId} onChange={v => set('plantId', v)}
                                options={[{ value: '', label: 'Selecione...' }, ...plants.map(p => ({ value: String(p.id), label: p.name }))]} />
                        </div>
                        <div style={{ display: 'flex', gap: 12 }}>
                            <FormSelect label="Tipo *" value={form.equipmentType} onChange={handleTypeChange} options={equipmentTypes} />
                            <FormSelect label="Status *" value={form.statusCode} onChange={v => set('statusCode', v)}
                                options={[
                                    { value: 'AVAILABLE', label: 'Disponível' },
                                    { value: 'IN_USE', label: 'Em Uso' },
                                    { value: 'IN_REPAIR', label: 'No Conserto' },
                                ]} />
                        </div>
                        <div style={{ display: 'flex', gap: 12 }}>
                            <FormSelect label="Fabricante (Catálogo)" value={form.manufacturer} onChange={handleManufacturerChange}
                                options={[{ value: '', label: 'Livre / Outro...' }, ...manufacturers.map(m => ({ value: m.name, label: m.name }))]} />
                            <FormSelect label="Modelo (Catálogo)" value={form.model} onChange={v => set('model', v)} disabled={!form.manufacturer}
                                options={[{ value: '', label: 'Selecione o modelo...' }, ...models.map(m => ({ value: m.name, label: m.name }))]} />
                        </div>
                        <div style={{ display: 'flex', gap: 12 }}>
                            <FormInput label="Data de Fabrico" type="date" value={form.manufactureDate} onChange={v => set('manufactureDate', v)} style={{ flex: 1 }} />
                            <FormInput label="Cor" value={form.color} onChange={v => set('color', v)} style={{ flex: 1 }} />
                        </div>
                        <div style={{ display: 'flex', gap: 12 }}>
                            <FormSelect label="Origem / Entrada *" value={form.sourceType} onChange={v => set('sourceType', v)}
                                options={[
                                    { value: 'MANUAL_PURCHASE', label: 'Nova Compra' },
                                    { value: 'MANUAL_REGISTRATION', label: 'Registo Manual / Estoque Antigo' },
                                    { value: 'IMPORTED_LEGACY', label: 'Importado de Sistema Legado' }
                                ]} />
                            <div style={{ flex: 1 }} />
                        </div>
                        <FormTextarea label="Observações do Lote (Comum)" value={form.notes} onChange={v => set('notes', v)} rows={2} />
                    </div>

                    {/* Right Col: Acquisition & Warranty */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
                        <SectionCard title="Compra e Garantia" icon={<span style={{fontSize: '1rem'}}>📋</span>}>
                            
                            <FormCheckbox label="Informações indisponíveis" checked={purchase.purchaseInfoUnavailable} onChange={e => {
                                setPur('purchaseInfoUnavailable', e);
                                if (e) setWar('warrantyInfoUnavailable', true);
                            }} id="batchPurchaseUnavailable" style={{ marginBottom: 12 }} />

                            {!purchase.purchaseInfoUnavailable ? (
                                <>
                                    <div style={{ display: 'flex', gap: 12 }}>
                                        <FormInput label="Data de Aquisição *" type="date" value={purchase.acquisitionDate} onChange={v => setPur('acquisitionDate', v)} style={{ flex: 1 }} />
                                        <div style={{ display: 'flex', gap: 8, flex: 1 }}>
                                            <FormInput label="Valor Unitário *" type="number" value={purchase.purchaseAmount} onChange={v => setPur('purchaseAmount', v)} style={{ flex: 2 }} />
                                            <FormSelect label="Moeda" value={purchase.currency} onChange={v => setPur('currency', v)} options={[{ value: 'AOA', label: 'AOA' }, { value: 'USD', label: 'USD' }, { value: 'EUR', label: 'EUR' }]} style={{ flex: 1 }} />
                                        </div>
                                    </div>
                                    <div style={{ display: 'flex', gap: 12, marginTop: 12 }}>
                                        <FormInput label="Nº Nota / Guia *" value={purchase.invoiceNumber} onChange={v => setPur('invoiceNumber', v)} style={{ flex: 1 }} />
                                        <div style={{ flex: 1 }}>
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
                                    <div style={{ display: 'flex', gap: 12, marginTop: 12 }}>
                                        <FormInput label="Nº Ordem de Compra" value={purchase.purchaseOrderNumber} onChange={v => setPur('purchaseOrderNumber', v)} style={{ flex: 1 }} />
                                        <div style={{ flex: 1 }}>
                                            <FileUpload
                                                label="Documento da Compra *"
                                                file={purchaseDocFile}
                                                onChange={(file) => setPurchaseDocFile(file)}
                                                accept=".pdf,.jpg,.jpeg,.png"
                                                maxSizeMB={10}
                                            />
                                        </div>
                                    </div>

                                    <div style={{ borderTop: '1px solid #e5e7eb', margin: '20px 0' }} />

                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                        <h4 style={{ fontSize: '14px', fontWeight: 600, color: '#374151', margin: 0 }}>Garantia</h4>
                                        <FormCheckbox label="Garantia Indisponível" checked={warranty.warrantyInfoUnavailable} onChange={e => setWar('warrantyInfoUnavailable', e)} id="batchWarrantyUnavailable" />
                                    </div>

                                    {!warranty.warrantyInfoUnavailable ? (
                                        <>
                                            <div style={{ display: 'flex', gap: 12 }}>
                                                <FormInput label="Garantia (Meses)" type="number" value={warranty.warrantyMonths} onChange={v => setWar('warrantyMonths', v)} style={{ flex: 1 }} />
                                                <FormInput label="Início da Garantia" type="date" value={warranty.warrantyStartDate} onChange={v => setWar('warrantyStartDate', v)} style={{ flex: 1 }} />
                                            </div>
                                            <div style={{ display: 'flex', gap: 12, marginTop: 12 }}>
                                                <FormInput label="Fim da Garantia" type="date" value={warranty.warrantyEndDate} onChange={v => setWar('warrantyEndDate', v)} style={{ flex: 1 }} />
                                                <div style={{ flex: 1 }} />
                                            </div>
                                            <div style={{ marginTop: 12 }}>
                                                <FormTextarea label="Notas da Garantia" value={warranty.warrantyNotes} onChange={v => setWar('warrantyNotes', v)} rows={1} />
                                            </div>
                                        </>
                                    ) : (
                                        <FormTextarea label="Motivo da indisponibilidade (Garantia) *" value={warranty.warrantyInfoUnavailableReason} onChange={v => setWar('warrantyInfoUnavailableReason', v)} rows={2} />
                                    )}
                                </>
                            ) : (
                                <FormTextarea label="Motivo da indisponibilidade (Compra/Garantia) *" value={purchase.purchaseInfoUnavailableReason} onChange={v => setPur('purchaseInfoUnavailableReason', v)} rows={3} />
                            )}
                        </SectionCard>
                    </div>
                </div>

                {/* Items Table */}
                <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                        <h3 style={{ fontSize: '15px', fontWeight: 600, color: '#111827' }}>Itens Individuais</h3>
                        <div style={{ fontSize: '13px', color: '#4b5563', fontWeight: 500 }}>
                            Itens individuais gerados: <span style={{ color: '#1e3a8a', fontWeight: 'bold' }}>{form.quantity}</span>
                        </div>
                    </div>

                    <div style={{ maxHeight: '350px', overflowY: 'auto', border: '1px solid #e5e7eb', borderRadius: '6px' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '13px' }}>
                            <thead style={{ position: 'sticky', top: 0, backgroundColor: '#f9fafb', zIndex: 1, boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                                <tr>
                                    <th style={{ padding: '12px', borderBottom: '1px solid #e5e7eb', width: '40px' }}>#</th>
                                    <th style={{ padding: '12px', borderBottom: '1px solid #e5e7eb' }}>Nº Série</th>
                                    <th style={{ padding: '12px', borderBottom: '1px solid #e5e7eb' }}>Hostname</th>
                                    <th style={{ padding: '12px', borderBottom: '1px solid #e5e7eb' }}>MAC Address</th>
                                    <th style={{ padding: '12px', borderBottom: '1px solid #e5e7eb' }}>ID Card</th>
                                </tr>
                            </thead>
                            <tbody>
                                {items.map((item, idx) => (
                                    <tr key={item.id} style={{ borderBottom: '1px solid #e5e7eb' }}>
                                        <td style={{ padding: '12px', color: '#6b7280' }}>{idx + 1}</td>
                                        <td style={{ padding: '8px' }}>
                                            <input value={item.serialNumber} onChange={e => updateItem(idx, 'serialNumber', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                        </td>
                                        <td style={{ padding: '8px' }}>
                                            <input value={item.hostname} onChange={e => updateItem(idx, 'hostname', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                        </td>
                                        <td style={{ padding: '8px' }}>
                                            <input value={item.macAddress} onChange={e => updateItem(idx, 'macAddress', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                        </td>
                                        <td style={{ padding: '8px' }}>
                                            <input value={item.idCard} onChange={e => updateItem(idx, 'idCard', e.target.value)} style={{ ...inputStyle, padding: '6px 8px' }} placeholder="Opcional" />
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px', paddingTop: '16px', borderTop: '1px solid #e5e7eb' }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle} disabled={saving}>Cancelar</button>
                    <SubmitBtn label={`Criar lote com ${form.quantity || 0} equipamentos`} loading={saving} disabled={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
