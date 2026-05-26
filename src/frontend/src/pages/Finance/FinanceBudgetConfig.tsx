import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Save, AlertTriangle, AlertCircle, CheckCircle, Settings2, Plus, Trash2, Power, PowerOff, Search } from 'lucide-react';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Lookup { id: number; name: string; }
interface Plant { id: number; name: string; companyId?: number; }
interface CostCenter { id: number; name: string; code?: string; companyId?: number; plantId?: number; }
interface Currency { id: number; code: string; }

interface BudgetConfigUI {
    _uid: string;
    id?: number;
    companyId: number | '';
    plantId: number | '';
    departmentId: number | '';
    costCenterId: number | 'GENERAL' | '';
    totalAmount: number;
    currencyId: number | '';
    year: number;
    isActive: boolean;
    _displayAmount: string;
}

// ─── Currency Formatting Helpers ──────────────────────────────────────────────

function parseFormattedNumber(str: string): number {
    if (!str || str.trim() === '') return 0;
    const cleaned = str.replace(/\s/g, '').replace(/\./g, '').replace(',', '.');
    const num = parseFloat(cleaned);
    return isNaN(num) ? 0 : num;
}

function formatNumberDisplay(value: number): string {
    if (value === 0) return '';
    const [intPart, decPart] = value.toFixed(2).split('.');
    const withThousands = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    if (decPart === '00') return withThousands;
    return `${withThousands},${decPart}`;
}

function formatInputValue(raw: string): string {
    if (!raw || raw.trim() === '') return '';
    let cleaned = raw.replace(/[^\d,.\s]/g, '');
    const lastComma = cleaned.lastIndexOf(',');
    const lastDot = cleaned.lastIndexOf('.');
    let intPart: string;
    let decPart: string | null = null;

    if (lastComma > lastDot) {
        intPart = cleaned.substring(0, lastComma).replace(/[,.\s]/g, '');
        decPart = cleaned.substring(lastComma + 1).replace(/\D/g, '');
    } else if (lastDot > lastComma) {
        const afterDot = cleaned.substring(lastDot + 1).replace(/\D/g, '');
        if (afterDot.length <= 2) {
            intPart = cleaned.substring(0, lastDot).replace(/[,.\s]/g, '');
            decPart = afterDot;
        } else {
            intPart = cleaned.replace(/[,.\s]/g, '');
        }
    } else {
        intPart = cleaned.replace(/[,.\s]/g, '');
    }

    if (!intPart && !decPart) return '';
    const formatted = (intPart || '0').replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    if (decPart !== null) return `${formatted},${decPart}`;
    return formatted;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function FinanceBudgetConfig() {
    const [companies, setCompanies] = useState<Lookup[]>([]);
    const [plants, setPlants] = useState<Plant[]>([]);
    const [departments, setDepartments] = useState<Lookup[]>([]);
    const [costCenters, setCostCenters] = useState<CostCenter[]>([]);
    const [currencies, setCurrencies] = useState<Currency[]>([]);

    const [configs, setConfigs] = useState<BudgetConfigUI[]>([]);
    const [year, setYear] = useState(new Date().getFullYear());
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
    const [searchQuery, setSearchQuery] = useState('');

    useEffect(() => { loadBaseData(); }, []);
    useEffect(() => {
        if (companies.length > 0) loadConfigs(year);
    }, [year, companies]);

    const loadBaseData = async () => {
        try {
            const [compRes, plantRes, depRes, ccRes, curRes] = await Promise.all([
                api.lookups.getCompanies(),
                api.lookups.getPlants(),
                api.lookups.getDepartments(),
                api.lookups.getCostCenters(),
                api.lookups.getCurrencies(),
            ]);
            setCompanies(compRes);
            setPlants(plantRes);
            setDepartments(depRes.filter((d: any) => !d.locked));
            setCostCenters(ccRes);
            setCurrencies(curRes);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar dados base.' });
        }
    };

    const loadConfigs = async (targetYear: number) => {
        setLoading(true);
        try {
            const data = await api.financeBudget.getConfig(targetYear);
            const mapped: BudgetConfigUI[] = data.map((item: any) => ({
                _uid: Math.random().toString(36).substring(2, 9),
                id: item.id,
                companyId: item.companyId,
                plantId: item.plantId,
                departmentId: item.departmentId,
                costCenterId: item.costCenterId == null ? 'GENERAL' : item.costCenterId,
                totalAmount: item.totalAmount,
                currencyId: item.currencyId,
                year: targetYear,
                isActive: item.isActive,
                _displayAmount: formatNumberDisplay(item.totalAmount)
            }));
            setConfigs(mapped);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar configurações.' });
        } finally {
            setLoading(false);
        }
    };

    const handleAddRow = () => {
        setConfigs([{
            _uid: Math.random().toString(36).substring(2, 9),
            companyId: '',
            plantId: '',
            departmentId: '',
            costCenterId: '',
            totalAmount: 0,
            currencyId: currencies[0]?.id || '',
            year: year,
            isActive: true,
            _displayAmount: ''
        }, ...configs]);
    };

    const handleRemoveRow = (uid: string) => {
        setConfigs(prev => prev.filter(c => c._uid !== uid));
    };

    const handleChange = (uid: string, field: keyof BudgetConfigUI, value: any) => {
        setConfigs(prev => prev.map(c => c._uid === uid ? { ...c, [field]: value } : c));
    };

    const handleAmountChange = (uid: string, rawValue: string) => {
        const formatted = formatInputValue(rawValue);
        const numericValue = parseFormattedNumber(rawValue);
        setConfigs(prev => prev.map(c => 
            c._uid === uid ? { ...c, _displayAmount: formatted, totalAmount: numericValue } : c
        ));
    };

    const handleAmountBlur = (uid: string) => {
        setConfigs(prev => prev.map(c => 
            c._uid === uid ? { ...c, _displayAmount: formatNumberDisplay(c.totalAmount) } : c
        ));
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            const invalid = configs.some(c => c.companyId === '' || c.plantId === '' || c.departmentId === '' || c.currencyId === '' || c.costCenterId === '');
            if (invalid) {
                setMessage({ type: 'error', text: 'Preencha todos os campos obrigatórios (Empresa, Planta, Departamento, Centro de Custo e Moeda) em todas as linhas.' });
                setSaving(false);
                return;
            }

            const payload = configs.map(c => ({
                id: c.id || 0,
                year: c.year,
                companyId: Number(c.companyId),
                plantId: Number(c.plantId),
                departmentId: Number(c.departmentId),
                costCenterId: c.costCenterId === 'GENERAL' ? null : Number(c.costCenterId),
                currencyId: Number(c.currencyId),
                totalAmount: c.totalAmount,
                isActive: c.isActive
            }));

            // Check for duplicates in the UI before sending
            const uniqueKeys = new Set();
            for (const c of payload) {
                const key = `${c.year}-${c.companyId}-${c.plantId}-${c.departmentId}-${c.costCenterId}-${c.currencyId}`;
                if (uniqueKeys.has(key)) {
                    setMessage({ type: 'error', text: 'Foram detectadas configurações duplicadas (mesma combinação de hierarquia e moeda). Remova ou consolide os duplicados.' });
                    setSaving(false);
                    return;
                }
                uniqueKeys.add(key);
            }

            await api.financeBudget.saveConfig(payload);
            setMessage({ type: 'success', text: 'Orçamentos guardados com sucesso.' });
            await loadConfigs(year);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao salvar orçamentos.' });
        } finally {
            setSaving(false);
        }
    };

    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '8px 10px',
        border: '1px solid var(--color-border)', borderRadius: '6px',
        backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text)',
        fontSize: '13px', fontWeight: 500, boxSizing: 'border-box', outline: 'none',
        transition: 'border-color 0.15s',
    };

    const selectStyle: React.CSSProperties = {
        ...inputStyle, cursor: 'pointer', appearance: 'auto',
    };

    const filteredConfigs = configs.filter(c => {
        if (!searchQuery) return true;
        const q = searchQuery.toLowerCase();
        const comp = companies.find(x => x.id === c.companyId)?.name.toLowerCase() || '';
        const pl = plants.find(x => x.id === c.plantId)?.name.toLowerCase() || '';
        const dep = departments.find(x => x.id === c.departmentId)?.name.toLowerCase() || '';
        const cc = c.costCenterId === 'GENERAL' ? 'geral' : costCenters.find(x => x.id === c.costCenterId)?.name.toLowerCase() || '';
        return comp.includes(q) || pl.includes(q) || dep.includes(q) || cc.includes(q);
    });

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '28px' }}>
            {/* ── Page Header ───────────────────────────────────────────────── */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '14px' }}>
                    <div style={{ width: 44, height: 44, borderRadius: '10px', backgroundColor: '#0284c71A', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#0284c7', flexShrink: 0 }}>
                        <Settings2 size={22} />
                    </div>
                    <div>
                        <h2 style={{ fontSize: '1.4rem', fontWeight: 900, color: 'var(--color-text)', margin: 0 }}>Configuração de Orçamento</h2>
                        <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                            Matriz hierárquica de limites de gastos (Empresa → Planta → Depto → CC).
                        </p>
                    </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '8px', padding: '6px 12px' }}>
                        <label style={{ fontSize: '13px', fontWeight: 700, color: 'var(--color-text-muted)', whiteSpace: 'nowrap' }}>Ano Fiscal:</label>
                        <select
                            value={year}
                            onChange={e => setYear(parseInt(e.target.value))}
                            style={{ border: 'none', background: 'transparent', fontSize: '14px', fontWeight: 700, color: 'var(--color-text)', cursor: 'pointer', outline: 'none' }}
                        >
                            {[year - 1, year, year + 1, year + 2].map(y => (
                                <option key={y} value={y}>{y}</option>
                            ))}
                        </select>
                    </div>
                    <button
                        onClick={handleAddRow}
                        disabled={saving || loading}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '10px 16px', backgroundColor: 'var(--color-bg-surface)', color: '#0284c7', border: '1px solid #e0f2fe', borderRadius: '8px', fontWeight: 700, fontSize: '13px', cursor: 'pointer' }}
                    >
                        <Plus size={16} /> Adicionar Linha
                    </button>
                    <button
                        onClick={handleSave}
                        disabled={saving || loading}
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 20px', backgroundColor: saving || loading ? '#93c5fd' : '#0284c7', color: '#fff', border: 'none', borderRadius: '8px', fontWeight: 700, fontSize: '14px', cursor: saving || loading ? 'not-allowed' : 'pointer' }}
                    >
                        <Save size={16} /> {saving ? 'A Guardar...' : 'Guardar Alterações'}
                    </button>
                </div>
            </div>

            {/* ── Feedback Bar ──────────────────────────────────────────────── */}
            {message && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', padding: '14px 18px', borderRadius: '10px', border: `1px solid ${message.type === 'error' ? '#fca5a5' : '#86efac'}`, backgroundColor: message.type === 'error' ? '#fef2f2' : '#f0fdf4', color: message.type === 'error' ? '#b91c1c' : '#15803d', fontWeight: 600, fontSize: '14px' }}>
                    {message.type === 'error' ? <AlertCircle size={18} /> : <CheckCircle size={18} />} {message.text}
                </div>
            )}

            {/* ── Top Filters / Search ────────────────────────────────────── */}
            <div style={{ display: 'flex', justifyContent: 'flex-start', alignItems: 'center' }}>
                <div style={{ position: 'relative', width: '300px' }}>
                    <Search size={16} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-muted)' }} />
                    <input
                        type="text"
                        placeholder="Pesquisar por empresa, planta, depto..."
                        value={searchQuery}
                        onChange={e => setSearchQuery(e.target.value)}
                        style={{ ...inputStyle, paddingLeft: '36px', borderRadius: '8px' }}
                    />
                </div>
            </div>

            {/* ── Table ─────────────────────────────────────────────────────── */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '1000px' }}>
                        <thead>
                            <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                                {['Empresa', 'Planta', 'Departamento', 'Centro de Custo', 'Moeda', 'Montante Anual', 'Estado', 'Ações'].map((h, i) => (
                                    <th key={h} style={{ padding: '14px 16px', fontSize: '11px', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em', textAlign: (i >= 5 && i <= 7) ? 'center' : 'left', whiteSpace: 'nowrap' }}>
                                        {h}
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={8} style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 600 }}>A carregar configurações...</td>
                                </tr>
                            ) : filteredConfigs.length === 0 ? (
                                <tr>
                                    <td colSpan={8} style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 600 }}>Nenhuma configuração encontrada.</td>
                                </tr>
                            ) : (
                                filteredConfigs.map((conf, idx) => {
                                    const availablePlants = conf.companyId ? plants.filter(p => p.companyId === conf.companyId || !p.companyId) : plants;
                                    const availableCostCenters = costCenters.filter(c => (!conf.companyId || c.companyId === conf.companyId) && (!conf.plantId || c.plantId === conf.plantId));

                                    return (
                                        <tr key={conf._uid} style={{ borderBottom: idx < filteredConfigs.length - 1 ? '1px solid var(--color-border)' : 'none', backgroundColor: !conf.isActive ? '#f8fafc' : 'transparent', transition: 'background-color 0.15s' }}>
                                            <td style={{ padding: '10px 16px', width: '14%' }}>
                                                <select style={selectStyle} value={conf.companyId} onChange={e => handleChange(conf._uid, 'companyId', e.target.value ? Number(e.target.value) : '')} disabled={!conf.isActive}>
                                                    <option value="">Selecione...</option>
                                                    {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                                </select>
                                            </td>
                                            <td style={{ padding: '10px 16px', width: '14%' }}>
                                                <select style={selectStyle} value={conf.plantId} onChange={e => handleChange(conf._uid, 'plantId', e.target.value ? Number(e.target.value) : '')} disabled={!conf.isActive}>
                                                    <option value="">Selecione...</option>
                                                    {availablePlants.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                                </select>
                                            </td>
                                            <td style={{ padding: '10px 16px', width: '16%' }}>
                                                <select style={selectStyle} value={conf.departmentId} onChange={e => handleChange(conf._uid, 'departmentId', e.target.value ? Number(e.target.value) : '')} disabled={!conf.isActive}>
                                                    <option value="">Selecione...</option>
                                                    {departments.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                                </select>
                                            </td>
                                            <td style={{ padding: '10px 16px', width: '18%' }}>
                                                <select style={selectStyle} value={conf.costCenterId} onChange={e => handleChange(conf._uid, 'costCenterId', e.target.value === 'GENERAL' ? 'GENERAL' : (e.target.value ? Number(e.target.value) : ''))} disabled={!conf.isActive}>
                                                    <option value="">Selecione...</option>
                                                    <option value="GENERAL">Orçamento Geral (Não Alocado)</option>
                                                    {availableCostCenters.map(c => <option key={c.id} value={c.id}>{c.code ? `${c.code} - ` : ''}{c.name}</option>)}
                                                </select>
                                            </td>
                                            <td style={{ padding: '10px 16px', width: '9%' }}>
                                                <select style={selectStyle} value={conf.currencyId} onChange={e => handleChange(conf._uid, 'currencyId', e.target.value ? Number(e.target.value) : '')} disabled={!conf.isActive}>
                                                    <option value="">Moeda...</option>
                                                    {currencies.map(c => <option key={c.id} value={c.id}>{c.code}</option>)}
                                                </select>
                                            </td>
                                            <td style={{ padding: '10px 16px', width: '15%' }}>
                                                <input type="text" inputMode="decimal" value={conf._displayAmount} onChange={e => handleAmountChange(conf._uid, e.target.value)} onBlur={() => handleAmountBlur(conf._uid)} placeholder="0,00" style={{ ...inputStyle, textAlign: 'right' }} disabled={!conf.isActive} />
                                            </td>
                                            <td style={{ padding: '10px 16px', textAlign: 'center', width: '8%' }}>
                                                <span style={{ display: 'inline-flex', alignItems: 'center', padding: '4px 10px', borderRadius: '999px', fontSize: '11px', fontWeight: 700, backgroundColor: conf.isActive ? '#dcfce7' : '#f1f5f9', color: conf.isActive ? '#166534' : '#64748b' }}>
                                                    {conf.isActive ? 'Ativo' : 'Inativo'}
                                                </span>
                                            </td>
                                            <td style={{ padding: '10px 16px', textAlign: 'center', width: '6%' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px' }}>
                                                    <button onClick={() => handleChange(conf._uid, 'isActive', !conf.isActive)} title={conf.isActive ? "Desativar" : "Reativar"} style={{ background: 'none', border: 'none', cursor: 'pointer', color: conf.isActive ? '#64748b' : '#10b981', padding: '4px' }}>
                                                        {conf.isActive ? <PowerOff size={16} /> : <Power size={16} />}
                                                    </button>
                                                    {!conf.id && (
                                                        <button onClick={() => handleRemoveRow(conf._uid)} title="Remover Linha Não Guardada" style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#ef4444', padding: '4px' }}>
                                                            <Trash2 size={16} />
                                                        </button>
                                                    )}
                                                </div>
                                            </td>
                                        </tr>
                                    );
                                })
                            )}
                        </tbody>
                    </table>
                </div>

                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', padding: '16px 20px', borderTop: '1px solid var(--color-border)', backgroundColor: '#eff6ff' }}>
                    <AlertTriangle size={18} style={{ color: '#0284c7', flexShrink: 0, marginTop: '1px' }} />
                    <p style={{ margin: 0, fontSize: '13px', color: '#1e40af', lineHeight: 1.55, fontWeight: 500 }}>
                        A combinação de Empresa, Planta, Departamento, Centro de Custo e Moeda forma a chave única do orçamento. 
                        Utilize "Orçamento Geral (Não Alocado)" para gastos do departamento que não são atribuídos a um centro de custo específico.
                        Linhas recém-adicionadas podem ser removidas, mas linhas já salvas devem ser desativadas.
                    </p>
                </div>
            </div>
        </div>
    );
}
