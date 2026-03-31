import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../lib/api';
import { LookupDto, CurrencyDto, UserDto } from '../../types';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { ROLES } from '../../constants/roles';
import { Edit2, Power, PowerOff } from 'lucide-react';

function debounce<T extends (...args: any[]) => void>(fn: T, delay: number) {
    let timeoutId: number | undefined;
    return (...args: Parameters<T>) => {
        if (timeoutId) clearTimeout(timeoutId);
        timeoutId = window.setTimeout(() => fn(...args), delay);
    };
}

export function MasterData() {
    const [units, setUnits] = useState<LookupDto[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [suppliers, setSuppliers] = useState<LookupDto[]>([]);
    const [costCenters, setCostCenters] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<LookupDto[]>([]);
    const [ivaRates, setIvaRates] = useState<any[]>([]);
    const [users, setUsers] = useState<UserDto[]>([]);
    const [loading, setLoading] = useState(true);

    // Form states
    const [activeTab, setActiveTab] = useState<'units' | 'currencies' | 'needLevels' | 'departments' | 'plants' | 'suppliers' | 'costCenters' | 'ivaRates'>('units');
    const [editMode, setEditMode] = useState<{ type: 'unit' | 'currency' | 'needLevel' | 'department' | 'plant' | 'supplier' | 'costCenter' | 'ivaRate', id: number | null }>({ type: 'unit', id: null });
    const [formData, setFormData] = useState({
        code: '',
        name: '',
        symbol: '',
        allowsDecimalQuantity: false,
        taxId: '',
        portalCode: '',
        primaveraCode: '',
        companyId: 0,
        responsibleUserId: '',
        ratePercent: 0
    });

    const [feedback, setFeedback] = useState<{ message: string, type: FeedbackType } | null>(null);
    const [validationErrors, setValidationErrors] = useState<{ name?: string, primaveraCode?: string }>({});

    const loadData = async () => {
        try {
            setLoading(true);
            const results = await Promise.allSettled([
                api.lookups.getUnits(true),
                api.lookups.getCurrencies(true),
                api.lookups.getNeedLevels(true),
                api.lookups.getDepartments(true),
                api.lookups.getPlants(undefined, true),
                api.lookups.getSuppliers(true),
                api.lookups.getCostCenters(true),
                api.lookups.getCompanies(true),
                api.lookups.getIvaRates(false),
                api.users.list()
            ]);

            const [uRes, cRes, nRes, dRes, pRes, sRes, ccRes, coRes, ivaRes, usersRes] = results;

            if (uRes.status === 'fulfilled') setUnits(uRes.value);
            if (cRes.status === 'fulfilled') setCurrencies(cRes.value);
            if (nRes.status === 'fulfilled') setNeedLevels(nRes.value);
            if (dRes.status === 'fulfilled') setDepartments(dRes.value);
            if (pRes.status === 'fulfilled') setPlants(pRes.value);
            if (sRes.status === 'fulfilled') setSuppliers(sRes.value);
            if (ccRes.status === 'fulfilled') setCostCenters(ccRes.value);
            if (coRes.status === 'fulfilled') setCompanies(coRes.value);
            if (ivaRes.status === 'fulfilled') setIvaRates(ivaRes.value);
            if (usersRes.status === 'fulfilled') setUsers(usersRes.value);

            // Check for failures
            const failures = results
                .map((res, i) => ({ res, i }))
                .filter(x => x.res.status === 'rejected')
                .map(x => {
                    const names = ['Unidades', 'Moedas', 'Níveis Necessidade', 'Departamentos', 'Plantas', 'Fornecedores', 'Centros Custo', 'Empresas', 'IVA', 'Utilizadores'];
                    return names[x.i];
                });

            if (failures.length > 0) {
                setFeedback({ 
                    message: `Aviso: Falha ao carregar alguns dados (${failures.join(', ')}). O sistema pode estar incompleto.`, 
                    type: 'error' 
                });
            }
        } catch (err: any) {
            setFeedback({ message: 'Erro crítico ao inicializar dados mestres.', type: 'error' });
        } finally {
            setLoading(false);
        }
    };

    const checkUniqueness = useCallback(
        debounce(async (name?: string, primaveraCode?: string, excludeId?: number) => {
            if (!name && !primaveraCode) return;

            try {
                const result = await api.lookups.checkSupplierUniqueness(name, primaveraCode, excludeId || undefined);
                setValidationErrors({
                    name: result.isNameDuplicate ? 'Este nome já está em uso.' : undefined,
                    primaveraCode: result.isPrimaveraDuplicate ? 'Este código Primavera já está em uso.' : undefined
                });
            } catch (err) {
                console.error('Uniqueness check failed', err);
            }
        }, 500),
        []
    );

    useEffect(() => {
        loadData();
    }, []);

    const handleEdit = (item: any, type: 'unit' | 'currency' | 'needLevel' | 'department' | 'plant' | 'supplier' | 'costCenter' | 'ivaRate') => {
        setEditMode({ type, id: item.id });
        setValidationErrors({});
        setFormData({
            code: item.code || '',
            name: item.name || '',
            symbol: item.symbol || '',
            allowsDecimalQuantity: item.allowsDecimalQuantity || false,
            taxId: item.taxId || '',
            portalCode: item.portalCode || item.code || '',
            primaveraCode: item.primaveraCode || '',
            // Plants use companyId; CostCenters reuse companyId to carry plantId
            companyId: type === 'costCenter' ? (item.plantId || 0) : (item.companyId || 0),
            responsibleUserId: item.responsibleUserId || '',
            ratePercent: item.ratePercent || 0
        });
    };

    const handleCancel = () => {
        let defaultType: 'unit' | 'currency' | 'needLevel' | 'department' | 'plant' | 'supplier' | 'costCenter' | 'ivaRate' = 'unit';
        if (activeTab === 'currencies') defaultType = 'currency';
        if (activeTab === 'needLevels') defaultType = 'needLevel';
        if (activeTab === 'departments') defaultType = 'department';
        if (activeTab === 'plants') defaultType = 'plant';
        if (activeTab === 'suppliers') defaultType = 'supplier';
        if (activeTab === 'costCenters') defaultType = 'costCenter';
        if (activeTab === 'ivaRates') defaultType = 'ivaRate';

        setEditMode({ type: defaultType, id: null });
        setValidationErrors({});
        setFormData({ code: '', name: '', symbol: '', allowsDecimalQuantity: false, taxId: '', portalCode: '', primaveraCode: '', companyId: 0, responsibleUserId: '', ratePercent: 0 });
    };

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        setFeedback(null);

        if (activeTab === 'suppliers' && (validationErrors.name || validationErrors.primaveraCode)) {
            setFeedback({ message: 'Por favor, corrija os erros de duplicidade antes de salvar.', type: 'error' });
            return;
        }

        try {
            if (activeTab === 'units') {
                if (editMode.id) {
                    await api.lookups.updateUnit(editMode.id, { code: formData.code, name: formData.name, allowsDecimalQuantity: formData.allowsDecimalQuantity });
                } else {
                    await api.lookups.createUnit({ code: formData.code, name: formData.name, allowsDecimalQuantity: formData.allowsDecimalQuantity });
                }
            } else if (activeTab === 'currencies') {
                if (editMode.id) {
                    await api.lookups.updateCurrency(editMode.id, { code: formData.code, symbol: formData.symbol });
                } else {
                    await api.lookups.createCurrency({ code: formData.code, symbol: formData.symbol });
                }
            } else if (activeTab === 'needLevels') {
                if (editMode.id) {
                    await api.lookups.updateNeedLevel(editMode.id, { code: formData.code, name: formData.name });
                } else {
                    await api.lookups.createNeedLevel({ code: formData.code, name: formData.name });
                }
            } else if (activeTab === 'departments') {
                const deptPayload = { code: formData.code, name: formData.name, responsibleUserId: formData.responsibleUserId || null as any };
                if (editMode.id) {
                    await api.lookups.updateDepartment(editMode.id, deptPayload);
                } else {
                    await api.lookups.createDepartment(deptPayload);
                }
            } else if (activeTab === 'plants') {
                if (editMode.id) {
                    await api.lookups.updatePlant(editMode.id, { code: formData.code, name: formData.name, companyId: formData.companyId });
                } else {
                    await api.lookups.createPlant({ code: formData.code, name: formData.name, companyId: formData.companyId });
                }
            } else if (activeTab === 'costCenters') {
                if (editMode.id) {
                    // companyId field carries plantId for CostCenters
                    await api.lookups.updateCostCenter(editMode.id, { code: formData.code, name: formData.name, companyId: formData.companyId });
                } else {
                    await api.lookups.createCostCenter({ code: formData.code, name: formData.name, companyId: formData.companyId });
                }
            } else if (activeTab === 'suppliers') {
                const supplierPayload = {
                    name: formData.name,
                    taxId: formData.taxId,
                    portalCode: formData.portalCode,
                    primaveraCode: formData.primaveraCode
                };
                if (editMode.id) {
                    await api.lookups.updateSupplier(editMode.id, supplierPayload);
                } else {
                    await api.lookups.createSupplier(supplierPayload);
                }
            } else if (activeTab === 'ivaRates') {
                if (editMode.id) {
                    await api.lookups.updateIvaRate(editMode.id, { code: formData.code, name: formData.name, ratePercent: formData.ratePercent });
                } else {
                    await api.lookups.createIvaRate({ code: formData.code, name: formData.name, ratePercent: formData.ratePercent });
                }
            }
            await loadData();
            setFeedback({ message: 'Registo salvo com sucesso.', type: 'success' });
            handleCancel();
        } catch (err: any) {
            setFeedback({ message: err.message || 'Erro ao salvar o registo.', type: 'error' });
        }
    };

    const handleToggleActive = async (id: number, type: 'unit' | 'currency' | 'needLevel' | 'department' | 'plant' | 'supplier' | 'costCenter' | 'ivaRate') => {
        try {
            setFeedback(null);
            if (type === 'unit') await api.lookups.toggleUnit(id);
            else if (type === 'currency') await api.lookups.toggleCurrency(id);
            else if (type === 'needLevel') await api.lookups.toggleNeedLevel(id);
            else if (type === 'department') await api.lookups.toggleDepartment(id);
            else if (type === 'plant') await api.lookups.togglePlant(id);
            else if (type === 'costCenter') await api.lookups.toggleCostCenter(id);
            else if (type === 'ivaRate') await api.lookups.toggleIvaRate(id);
            else await api.lookups.toggleSupplier(id);
            await loadData();
            setFeedback({ message: 'Estado alterado com sucesso.', type: 'success' });
        } catch (err: any) {
            setFeedback({ message: err.message || 'Erro ao alterar estado.', type: 'error' });
        }
    };

    if (loading && units.length === 0) return <div className="p-4">A carregar...</div>;

    return (
        <div className="p-6 max-w-5xl mx-auto relative">
            <div className="sticky top-0 z-50 mb-4 bg-white/80 backdrop-blur-sm -mx-6 px-6 pt-2">
                {feedback && (
                    <Feedback
                        message={feedback.message}
                        type={feedback.type}
                        onClose={() => setFeedback(null)}
                    />
                )}
            </div>

            {/* Header matches RequestsList.tsx style */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0, marginBottom: '32px' }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Dados Mestres</h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Gerenciamento de entidades e parâmetros fundamentais do sistema.</p>
                </div>
            </div>

            {/* Standard Underline Tab Navigation */}
            <div style={{ display: 'flex', gap: '8px', marginBottom: '32px', borderBottom: '2px solid var(--color-border)', paddingBottom: '2px', overflowX: 'auto' }}>
                {[
                    { id: 'units', label: 'Unidades' },
                    { id: 'currencies', label: 'Moedas' },
                    { id: 'needLevels', label: 'Graus de Necessidade' },
                    { id: 'departments', label: 'Departamentos' },
                    { id: 'plants', label: 'Plantas' },
                    { id: 'suppliers', label: 'Fornecedores' },
                    { id: 'costCenters', label: 'Centros de Custo' },
                    { id: 'ivaRates', label: 'Taxas de IVA' }
                ].map((tab) => (
                    <button
                        key={tab.id}
                        className={activeTab === tab.id ? '' : 'hover:text-primary'}
                        style={{
                            padding: '12px 20px',
                            backgroundColor: 'transparent',
                            border: 'none',
                            borderBottom: activeTab === tab.id ? '4px solid var(--color-primary)' : '4px solid transparent',
                            color: activeTab === tab.id ? 'var(--color-primary)' : 'var(--color-text-muted)',
                            fontWeight: 800,
                            fontSize: '0.75rem',
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em',
                            cursor: 'pointer',
                            transition: 'all 0.15s ease',
                            whiteSpace: 'nowrap',
                            marginBottom: '-2px'
                        }}
                        onClick={() => { setActiveTab(tab.id as any); handleCancel(); }}
                    >
                        {tab.label}
                    </button>
                ))}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(350px, 1fr) 2fr', gap: '32px', alignItems: 'start' }}>

                {/* Form Column - Styled as a standard card */}
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    padding: '32px',
                    borderRadius: 'var(--radius-md)',
                    boxShadow: 'var(--shadow-brutal)',
                    border: '2px solid var(--color-border-heavy)',
                    position: 'sticky',
                    top: 'calc(var(--header-height) + 1rem)'
                }}>
                    <h2 style={{
                        marginTop: 0,
                        marginBottom: '24px',
                        fontSize: '1.25rem',
                        fontWeight: 900,
                        textTransform: 'uppercase',
                        color: 'var(--color-primary)',
                        borderBottom: '2px solid var(--color-border)',
                        paddingBottom: '8px'
                    }}>
                        {editMode.id ? 'Editar' : 'Novo'} {
                            activeTab === 'units' ? 'Unidade' :
                                activeTab === 'currencies' ? 'Moeda' :
                                    activeTab === 'departments' ? 'Departamento' :
                                        activeTab === 'plants' ? 'Planta' :
                                            activeTab === 'costCenters' ? 'Centro de Custo' :
                                                activeTab === 'suppliers' ? 'Fornecedor' :
                                                    activeTab === 'ivaRates' ? 'Taxa de IVA' :
                                                        'Grau de Necessidade'
                        }
                    </h2>
                    <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                        {activeTab === 'suppliers' ? (
                            <>
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '6px' }}>Código Portal</label>
                                    <input
                                        disabled
                                        type="text"
                                        style={{
                                            width: '100%',
                                            padding: '12px',
                                            backgroundColor: 'var(--color-bg-page)',
                                            border: '2px solid var(--color-border)',
                                            fontSize: '0.85rem',
                                            fontWeight: 700,
                                            color: 'var(--color-text-muted)',
                                            cursor: 'not-allowed'
                                        }}
                                        value={editMode.id ? formData.portalCode : 'GERADO AUTOMÁTICO'}
                                    />
                                    <p style={{ marginTop: '4px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>Identificador interno sequencial.</p>
                                </div>
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Código Primavera</label>
                                    <input
                                        type="text"
                                        style={{
                                            width: '100%',
                                            padding: '12px',
                                            backgroundColor: 'white',
                                            border: `2px solid ${validationErrors.primaveraCode ? 'var(--color-status-rejected)' : 'var(--color-border)'}`,
                                            fontSize: '0.85rem',
                                            fontWeight: 600,
                                            outline: 'none'
                                        }}
                                        value={formData.primaveraCode}
                                        onChange={e => {
                                            const val = e.target.value;
                                            setFormData({ ...formData, primaveraCode: val });
                                            checkUniqueness(undefined, val, editMode.id || undefined);
                                        }}
                                        placeholder="OPCIONAL"
                                    />
                                    {validationErrors.primaveraCode && (
                                        <p style={{ marginTop: '4px', fontSize: '0.7rem', color: 'var(--color-status-rejected)', fontWeight: 700 }}>{validationErrors.primaveraCode}</p>
                                    )}
                                </div>
                            </>
                        ) : (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Código</label>
                                <input
                                    required
                                    type="text"
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.code}
                                    onChange={e => setFormData({ ...formData, code: e.target.value })}
                                />
                            </div>
                        )}

                        {activeTab === 'currencies' ? (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Símbolo</label>
                                <input
                                    required
                                    type="text"
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.symbol}
                                    onChange={e => setFormData({ ...formData, symbol: e.target.value })}
                                />
                            </div>
                        ) : (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Nome/Descrição</label>
                                <input
                                    required
                                    type="text"
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: `2px solid ${activeTab === 'suppliers' && validationErrors.name ? 'var(--color-status-rejected)' : 'var(--color-border)'}`,
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.name}
                                    onChange={e => {
                                        const val = e.target.value;
                                        setFormData({ ...formData, name: val });
                                        if (activeTab === 'suppliers') {
                                            checkUniqueness(val, undefined, editMode.id || undefined);
                                        }
                                    }}
                                />
                                {activeTab === 'suppliers' && validationErrors.name && (
                                    <p style={{ marginTop: '4px', fontSize: '0.7rem', color: 'var(--color-status-rejected)', fontWeight: 700 }}>{validationErrors.name}</p>
                                )}
                            </div>
                        )}

                        {activeTab === 'ivaRates' && (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Percentagem (%)</label>
                                <input
                                    required
                                    type="number"
                                    min="0"
                                    max="100"
                                    step="any"
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.ratePercent}
                                    onChange={e => setFormData({ ...formData, ratePercent: parseFloat(e.target.value) || 0 })}
                                />
                            </div>
                        )}

                        {activeTab === 'suppliers' && (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>NIF (Opcional)</label>
                                <input
                                    type="text"
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.taxId}
                                    onChange={e => setFormData({ ...formData, taxId: e.target.value })}
                                />
                            </div>
                        )}

                        {activeTab === 'units' && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '4px' }}>
                                <input
                                    id="allowsDecimalQuantity"
                                    type="checkbox"
                                    style={{ width: '18px', height: '18px', cursor: 'pointer' }}
                                    checked={formData.allowsDecimalQuantity}
                                    onChange={e => setFormData({ ...formData, allowsDecimalQuantity: e.target.checked })}
                                />
                                <label htmlFor="allowsDecimalQuantity" style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', cursor: 'pointer' }}>
                                    Permite Qtde Fracionada (Decimais)
                                </label>
                            </div>
                        )}

                        {activeTab === 'plants' && (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Empresa Responsável</label>
                                <select
                                    required
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.companyId}
                                    onChange={e => setFormData({ ...formData, companyId: parseInt(e.target.value) })}
                                >
                                    <option value="">Selecione a Empresa...</option>
                                    {companies.map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                            </div>
                        )}

                        {activeTab === 'costCenters' && (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Planta Vinculada <span style={{ color: 'red' }}>*</span></label>
                                <select
                                    required
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.companyId}
                                    onChange={e => setFormData({ ...formData, companyId: parseInt(e.target.value) })}
                                >
                                    <option value="">Selecione a Planta...</option>
                                    {plants.map((p: any) => (
                                        <option key={p.id} value={p.id}>{p.name}</option>
                                    ))}
                                </select>
                            </div>
                        )}

                        {activeTab === 'departments' && (
                            <div>
                                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px' }}>Responsável (Aprovador de Área)</label>
                                <select
                                    style={{
                                        width: '100%',
                                        padding: '12px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        outline: 'none'
                                    }}
                                    value={formData.responsibleUserId}
                                    onChange={e => setFormData({ ...formData, responsibleUserId: e.target.value })}
                                >
                                    <option value="">Selecione o Responsável...</option>
                                    {users.filter(u => u.roles?.includes(ROLES.AREA_APPROVER)).map(u => (
                                        <option key={u.id} value={u.id}>{u.fullName}</option>
                                    ))}
                                </select>
                                <p style={{ marginTop: '4px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>Este usuário será auto-preenchido como "Aprovador de Área" nos pedidos criados para este departamento.</p>
                            </div>
                        )}

                        <div style={{ display: 'flex', gap: '12px', marginTop: '12px' }}>
                            <button
                                type="submit"
                                className="btn-primary"
                                style={{ flex: 1, height: '44px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px', borderRadius: '4px' }}
                            >
                                {editMode.id ? 'ATUALIZAR' : 'SALVAR'}
                            </button>
                            {editMode.id && (
                                <button
                                    type="button"
                                    onClick={handleCancel}
                                    style={{
                                        flex: 1,
                                        height: '44px',
                                        backgroundColor: 'white',
                                        border: '2px solid var(--color-border)',
                                        color: 'var(--color-text-main)',
                                        fontWeight: 800,
                                        fontSize: '0.75rem',
                                        textTransform: 'uppercase',
                                        cursor: 'pointer',
                                        borderRadius: '4px'
                                    }}
                                >
                                    CANCELAR
                                </button>
                            )}
                        </div>
                    </form>
                </div>

                {/* Table Column - Styled as a standard card */}
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    borderRadius: 'var(--radius-md)',
                    boxShadow: 'var(--shadow-brutal)',
                    border: '2px solid var(--color-border-heavy)',
                    overflow: 'hidden'
                }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', border: 'none' }}>
                        <thead style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                            <tr>
                                <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>
                                    {activeTab === 'suppliers' ? 'Cód. Portal' : 'ID'}
                                </th>
                                <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>
                                    {activeTab === 'suppliers' ? 'Cód. Primavera' : 'Código'}
                                </th>
                                <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>
                                    {activeTab === 'currencies' ? 'Símbolo' : 'Nome'}
                                </th>
                                {activeTab === 'suppliers' && (
                                    <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>NIF</th>
                                )}
                                {activeTab === 'plants' && (
                                    <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>Empresa</th>
                                )}
                                {activeTab === 'costCenters' && (
                                    <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>Planta/Unidade</th>
                                )}
                                {activeTab === 'ivaRates' && (
                                    <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>% Taxa</th>
                                )}
                                {activeTab === 'departments' && (
                                    <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>Responsável</th>
                                )}
                                <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>Estado</th>
                                <th style={{ padding: '16px', textAlign: 'right', fontSize: '0.7rem', fontWeight: 800, color: '#fff', textTransform: 'uppercase' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody style={{ backgroundColor: 'white' }}>
                            {activeTab === 'units' && units.map((u) => (
                                <tr key={u.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !u.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{u.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{u.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{u.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${u.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {u.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(u, 'unit')
                                                },
                                                {
                                                    label: u.isActive ? 'Desativar' : 'Ativar',
                                                    icon: u.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(u.id, 'unit')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'currencies' && currencies.map((c) => (
                                <tr key={c.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !c.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{c.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{c.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{c.symbol}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${c.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {c.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(c, 'currency')
                                                },
                                                {
                                                    label: c.isActive ? 'Desativar' : 'Ativar',
                                                    icon: c.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(c.id, 'currency')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'needLevels' && needLevels.map((n) => (
                                <tr key={n.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !n.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{n.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{n.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{n.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${n.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {n.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(n, 'needLevel')
                                                },
                                                {
                                                    label: n.isActive ? 'Desativar' : 'Ativar',
                                                    icon: n.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(n.id, 'needLevel')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'departments' && departments.map((d) => (
                                <tr key={d.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !d.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{d.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{d.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{d.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                                        {users.find(u => u.id === d.responsibleUserId)?.fullName || '-'}
                                    </td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${d.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {d.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(d, 'department')
                                                },
                                                {
                                                    label: d.isActive ? 'Desativar' : 'Ativar',
                                                    icon: d.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(d.id, 'department')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'plants' && plants.map((p) => (
                                <tr key={p.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !p.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{p.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{p.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{p.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{p.companyName}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${p.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {p.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(p, 'plant')
                                                },
                                                {
                                                    label: p.isActive ? 'Desativar' : 'Ativar',
                                                    icon: p.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(p.id, 'plant')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'costCenters' && costCenters.map((c) => (
                                <tr key={c.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !c.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{c.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{c.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{c.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>{(c as any).plantName || '-'}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${c.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {c.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(c, 'costCenter')
                                                },
                                                {
                                                    label: c.isActive ? 'Desativar' : 'Ativar',
                                                    icon: c.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(c.id, 'costCenter')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'suppliers' && suppliers.map((s) => (
                                <tr key={s.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !s.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)' }}>{s.portalCode}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{s.primaveraCode || '-'}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{s.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{s.taxId || '-'}</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${s.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {s.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(s, 'supplier')
                                                },
                                                {
                                                    label: s.isActive ? 'Desativar' : 'Ativar',
                                                    icon: s.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(s.id, 'supplier')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                            {activeTab === 'ivaRates' && ivaRates.map((i) => (
                                <tr key={i.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: !i.isActive ? 'rgba(var(--color-bg-page-rgb), 0.5)' : 'inherit' }}>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>{i.id}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{i.code}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 600 }}>{i.name}</td>
                                    <td style={{ padding: '16px', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-status-blue)' }}>{i.ratePercent}%</td>
                                    <td style={{ padding: '16px', fontSize: '0.8rem' }}>
                                        <span className={`badge ${i.isActive ? 'badge-success' : 'badge-neutral'}`}>
                                            {i.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                                        <KebabMenu
                                            options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={16} />,
                                                    onClick: () => handleEdit(i, 'ivaRate')
                                                },
                                                {
                                                    label: i.isActive ? 'Desativar' : 'Ativar',
                                                    icon: i.isActive ? <PowerOff size={16} /> : <Power size={16} />,
                                                    onClick: () => handleToggleActive(i.id, 'ivaRate')
                                                }
                                            ]}
                                        />
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}
