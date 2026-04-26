import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Save, AlertTriangle, AlertCircle, CheckCircle, Settings2 } from 'lucide-react';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Department { id: number; name: string; }
interface Currency { id: number; code: string; }
interface BudgetConfig {
    departmentId: number;
    totalAmount: number;
    currencyId: number;
    year: number;
}

// ─── Currency Formatting Helpers ──────────────────────────────────────────────

/** Parse a formatted string like "1 500 000,75" into a number (1500000.75). */
function parseFormattedNumber(str: string): number {
    if (!str || str.trim() === '') return 0;
    // Remove thousand separators (spaces and dots), convert decimal comma to dot
    const cleaned = str.replace(/\s/g, '').replace(/\./g, '').replace(',', '.');
    const num = parseFloat(cleaned);
    return isNaN(num) ? 0 : num;
}

/** Format a number into display string with thousand separators and decimal comma.
 *  Example: 1500000.75 → "1 500 000,75" */
function formatNumberDisplay(value: number): string {
    if (value === 0) return '';
    const [intPart, decPart] = value.toFixed(2).split('.');
    const withThousands = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    // Remove trailing ".00" for clean display
    if (decPart === '00') return withThousands;
    return `${withThousands},${decPart}`;
}

/** Format a raw input string by adding thousand separators while preserving cursor intent.
 *  Allows digits, spaces, commas, and dots only. */
function formatInputValue(raw: string): string {
    if (!raw || raw.trim() === '') return '';
    // Allow only digits, comma, dot, and spaces
    let cleaned = raw.replace(/[^\d,.\s]/g, '');
    // Find the decimal separator (last comma or last dot)
    const lastComma = cleaned.lastIndexOf(',');
    const lastDot = cleaned.lastIndexOf('.');
    let intPart: string;
    let decPart: string | null = null;

    if (lastComma > lastDot) {
        // Comma is the decimal separator
        intPart = cleaned.substring(0, lastComma).replace(/[,.\s]/g, '');
        decPart = cleaned.substring(lastComma + 1).replace(/\D/g, '');
    } else if (lastDot > lastComma) {
        // Dot might be decimal or thousand. If there are 1-2 digits after, treat as decimal.
        const afterDot = cleaned.substring(lastDot + 1).replace(/\D/g, '');
        if (afterDot.length <= 2) {
            intPart = cleaned.substring(0, lastDot).replace(/[,.\s]/g, '');
            decPart = afterDot;
        } else {
            // It's a thousand separator dot
            intPart = cleaned.replace(/[,.\s]/g, '');
        }
    } else {
        intPart = cleaned.replace(/[,.\s]/g, '');
    }

    if (!intPart && !decPart) return '';
    // Add thousand separators (space)
    const formatted = (intPart || '0').replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    if (decPart !== null) return `${formatted},${decPart}`;
    return formatted;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function FinanceBudgetConfig() {
    const [departments, setDepartments] = useState<Department[]>([]);
    const [currencies, setCurrencies] = useState<Currency[]>([]);
    const [configs, setConfigs] = useState<Record<number, BudgetConfig>>({});
    // Display values track the user-visible formatted string per department
    const [displayAmounts, setDisplayAmounts] = useState<Record<number, string>>({});
    const [year, setYear] = useState(new Date().getFullYear());
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

    useEffect(() => { loadBaseData(); }, []);
    useEffect(() => {
        if (departments.length > 0 && currencies.length > 0) loadConfigs(year);
    }, [year, departments, currencies]);

    const loadBaseData = async () => {
        try {
            const [depsRes, curRes] = await Promise.all([
                api.lookups.getDepartments(),
                api.lookups.getCurrencies(),
            ]);
            setDepartments(depsRes.filter((d: any) => !d.locked));
            setCurrencies(curRes);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar dados base.' });
        }
    };

    const loadConfigs = async (targetYear: number) => {
        setLoading(true);
        try {
            const data = await api.financeBudget.getConfig(targetYear);
            const map: Record<number, BudgetConfig> = {};
            const displayMap: Record<number, string> = {};
            data.forEach((item: any) => {
                map[item.departmentId] = {
                    departmentId: item.departmentId,
                    totalAmount: item.totalAmount,
                    currencyId: item.currencyId,
                    year: targetYear,
                };
                // Initialize display value from saved numeric value
                displayMap[item.departmentId] = formatNumberDisplay(item.totalAmount);
            });
            setConfigs(map);
            setDisplayAmounts(displayMap);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar configurações.' });
        } finally {
            setLoading(false);
        }
    };

    const handleAmountChange = (departmentId: number, rawValue: string) => {
        // Update the display string (formatted with thousand separators)
        const formatted = formatInputValue(rawValue);
        setDisplayAmounts(prev => ({ ...prev, [departmentId]: formatted }));
        // Parse into numeric for state/payload
        const numericValue = parseFormattedNumber(rawValue);
        setConfigs(prev => {
            const existing = prev[departmentId] || {
                departmentId, year, totalAmount: 0,
                currencyId: currencies[0]?.id || 0,
            };
            return { ...prev, [departmentId]: { ...existing, totalAmount: numericValue } };
        });
    };

    const handleAmountBlur = (departmentId: number) => {
        // On blur, re-format cleanly from the numeric value
        const conf = configs[departmentId];
        if (conf) {
            setDisplayAmounts(prev => ({
                ...prev,
                [departmentId]: formatNumberDisplay(conf.totalAmount),
            }));
        }
    };

    const handleCurrencyChange = (departmentId: number, value: number) => {
        setConfigs(prev => {
            const existing = prev[departmentId] || {
                departmentId, year, totalAmount: 0,
                currencyId: currencies[0]?.id || 0,
            };
            return { ...prev, [departmentId]: { ...existing, currencyId: value } };
        });
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            const payload = Object.values(configs).filter(c => c.totalAmount > 0);
            await api.financeBudget.saveConfig(payload);
            setMessage({ type: 'success', text: 'Orçamentos anuais guardados com sucesso.' });
            await loadConfigs(year);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao salvar orçamentos.' });
        } finally {
            setSaving(false);
        }
    };

    const configuredCount = departments.filter(d => (configs[d.id]?.totalAmount || 0) > 0).length;

    // ── Styles ────────────────────────────────────────────────────────────────

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '9px 12px',
        border: '1px solid var(--color-border)',
        borderRadius: '8px',
        backgroundColor: 'var(--color-bg-page)',
        color: 'var(--color-text)',
        fontSize: '14px',
        fontWeight: 500,
        boxSizing: 'border-box',
        outline: 'none',
        transition: 'border-color 0.15s',
    };

    const selectStyle: React.CSSProperties = {
        ...inputStyle,
        cursor: 'pointer',
        appearance: 'auto',
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '28px' }}>

            {/* ── Page Header ───────────────────────────────────────────────── */}
            <div style={{
                display: 'flex', justifyContent: 'space-between',
                alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px',
            }}>
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '14px' }}>
                    <div style={{
                        width: 44, height: 44, borderRadius: '10px',
                        backgroundColor: '#0284c71A',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        color: '#0284c7', flexShrink: 0,
                    }}>
                        <Settings2 size={22} />
                    </div>
                    <div>
                        <h2 style={{ fontSize: '1.4rem', fontWeight: 900, color: 'var(--color-text)', margin: 0 }}>
                            Configuração de Orçamento
                        </h2>
                        <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                            Defina o orçamento anual (Committed Spend limits) aprovado para cada departamento.
                        </p>
                    </div>
                </div>

                {/* Controls */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
                    {/* Year selector */}
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px',
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '1px solid var(--color-border)',
                        borderRadius: '8px', padding: '6px 12px',
                    }}>
                        <label style={{ fontSize: '13px', fontWeight: 700, color: 'var(--color-text-muted)', whiteSpace: 'nowrap' }}>
                            Ano Fiscal:
                        </label>
                        <select
                            value={year}
                            onChange={e => setYear(parseInt(e.target.value))}
                            style={{
                                border: 'none', background: 'transparent',
                                fontSize: '14px', fontWeight: 700,
                                color: 'var(--color-text)', cursor: 'pointer',
                                outline: 'none',
                            }}
                        >
                            {[year - 1, year, year + 1, year + 2].map(y => (
                                <option key={y} value={y}>{y}</option>
                            ))}
                        </select>
                    </div>

                    {/* Save button */}
                    <button
                        onClick={handleSave}
                        disabled={saving || loading}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '8px',
                            padding: '10px 20px',
                            backgroundColor: saving || loading ? '#93c5fd' : '#0284c7',
                            color: '#fff',
                            border: 'none', borderRadius: '8px',
                            fontWeight: 700, fontSize: '14px',
                            cursor: saving || loading ? 'not-allowed' : 'pointer',
                            transition: 'background-color 0.2s',
                            boxShadow: '0 1px 3px rgba(2,132,199,0.3)',
                        }}
                        onMouseEnter={e => { if (!saving && !loading) e.currentTarget.style.backgroundColor = '#0369a1'; }}
                        onMouseLeave={e => { if (!saving && !loading) e.currentTarget.style.backgroundColor = '#0284c7'; }}
                    >
                        <Save size={16} />
                        {saving ? 'A Guardar...' : 'Guardar Alterações'}
                    </button>
                </div>
            </div>

            {/* ── Feedback Bar ──────────────────────────────────────────────── */}
            {message && (
                <div style={{
                    display: 'flex', alignItems: 'center', gap: '10px',
                    padding: '14px 18px', borderRadius: '10px',
                    border: `1px solid ${message.type === 'error' ? '#fca5a5' : '#86efac'}`,
                    backgroundColor: message.type === 'error' ? '#fef2f2' : '#f0fdf4',
                    color: message.type === 'error' ? '#b91c1c' : '#15803d',
                    fontWeight: 600, fontSize: '14px',
                }}>
                    {message.type === 'error'
                        ? <AlertCircle size={18} />
                        : <CheckCircle size={18} />
                    }
                    {message.text}
                </div>
            )}

            {/* ── Progress Summary ──────────────────────────────────────────── */}
            {!loading && departments.length > 0 && (
                <div style={{
                    display: 'flex', gap: '12px',
                    padding: '16px 20px',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '10px',
                    alignItems: 'center',
                }}>
                    <div style={{ flex: 1 }}>
                        <div style={{ fontSize: '12px', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '6px' }}>
                            Departamentos Configurados
                        </div>
                        <div style={{
                            height: '6px', backgroundColor: '#e2e8f0',
                            borderRadius: '3px', overflow: 'hidden',
                        }}>
                            <div style={{
                                height: '100%',
                                width: `${departments.length ? (configuredCount / departments.length) * 100 : 0}%`,
                                backgroundColor: '#0284c7',
                                transition: 'width 0.5s ease-out',
                                borderRadius: '3px',
                            }} />
                        </div>
                    </div>
                    <div style={{ fontWeight: 900, fontSize: '1.2rem', color: '#0284c7', whiteSpace: 'nowrap' }}>
                        {configuredCount} / {departments.length}
                    </div>
                </div>
            )}

            {/* ── Table ─────────────────────────────────────────────────────── */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                borderRadius: '12px',
                boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
                overflow: 'hidden',
            }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                        {/* Head */}
                        <thead>
                            <tr style={{
                                backgroundColor: 'var(--color-bg-page)',
                                borderBottom: '2px solid var(--color-border)',
                            }}>
                                {['Departamento', 'Montante Anual', 'Moeda (Base)', 'Estado'].map((h, i) => (
                                    <th key={h} style={{
                                        padding: '14px 20px',
                                        fontSize: '11px', fontWeight: 800,
                                        color: 'var(--color-text-muted)',
                                        textTransform: 'uppercase', letterSpacing: '0.06em',
                                        textAlign: i === 3 ? 'center' : 'left',
                                        whiteSpace: 'nowrap',
                                    }}>
                                        {h}
                                    </th>
                                ))}
                            </tr>
                        </thead>

                        {/* Body */}
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={4} style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                        A carregar departamentos...
                                    </td>
                                </tr>
                            ) : departments.length === 0 ? (
                                <tr>
                                    <td colSpan={4} style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                        Nenhum departamento ativo encontrado.
                                    </td>
                                </tr>
                            ) : (
                                departments.map((dept, idx) => {
                                    const conf = configs[dept.id];
                                    const hasData = conf && conf.totalAmount > 0;
                                    return (
                                        <tr
                                            key={dept.id}
                                            style={{
                                                borderBottom: idx < departments.length - 1
                                                    ? '1px solid var(--color-border)'
                                                    : 'none',
                                                transition: 'background-color 0.15s',
                                            }}
                                            onMouseEnter={e => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                                            onMouseLeave={e => e.currentTarget.style.backgroundColor = 'transparent'}
                                        >
                                            {/* Department */}
                                            <td style={{ padding: '16px 20px' }}>
                                                <div style={{ fontWeight: 700, fontSize: '14px', color: 'var(--color-text)' }}>
                                                    {dept.name}
                                                </div>
                                                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                                    ID: {dept.id}
                                                </div>
                                            </td>

                                            {/* Amount */}
                                            <td style={{ padding: '12px 20px', width: '220px' }}>
                                                <input
                                                    type="text"
                                                    inputMode="decimal"
                                                    value={displayAmounts[dept.id] ?? ''}
                                                    onChange={e => handleAmountChange(dept.id, e.target.value)}
                                                    onBlur={() => handleAmountBlur(dept.id)}
                                                    placeholder="Ex: 1 500 000,00"
                                                    style={inputStyle}
                                                    onFocus={e => e.target.style.borderColor = '#0284c7'}
                                                />
                                            </td>

                                            {/* Currency */}
                                            <td style={{ padding: '12px 20px', width: '160px' }}>
                                                <select
                                                    value={conf?.currencyId || (currencies[0]?.id || '')}
                                                    onChange={e => handleCurrencyChange(dept.id, parseInt(e.target.value))}
                                                    style={selectStyle}
                                                    onFocus={e => e.target.style.borderColor = '#0284c7'}
                                                    onBlur={e => e.target.style.borderColor = 'var(--color-border)'}
                                                >
                                                    {currencies.map(c => (
                                                        <option key={c.id} value={c.id}>{c.code}</option>
                                                    ))}
                                                </select>
                                            </td>

                                            {/* Status */}
                                            <td style={{ padding: '12px 20px', textAlign: 'center' }}>
                                                <span style={{
                                                    display: 'inline-flex', alignItems: 'center', gap: '5px',
                                                    padding: '4px 12px', borderRadius: '999px',
                                                    fontSize: '12px', fontWeight: 700,
                                                    backgroundColor: hasData ? '#e0f2fe' : '#f1f5f9',
                                                    color: hasData ? '#0284c7' : '#64748b',
                                                    border: `1px solid ${hasData ? '#bae6fd' : '#e2e8f0'}`,
                                                }}>
                                                    {hasData
                                                        ? <><CheckCircle size={12} /> Configurado</>
                                                        : 'Não Atribuído'
                                                    }
                                                </span>
                                            </td>
                                        </tr>
                                    );
                                })
                            )}
                        </tbody>
                    </table>
                </div>

                {/* ── Footer Notice ─────────────────────────────────────────── */}
                <div style={{
                    display: 'flex', gap: '12px', alignItems: 'flex-start',
                    padding: '16px 20px',
                    borderTop: '1px solid var(--color-border)',
                    backgroundColor: '#eff6ff',
                }}>
                    <AlertTriangle size={18} style={{ color: '#0284c7', flexShrink: 0, marginTop: '1px' }} />
                    <p style={{ margin: 0, fontSize: '13px', color: '#1e40af', lineHeight: 1.55, fontWeight: 500 }}>
                        A alteração da moeda num orçamento existente recalculará os indicadores do painel de administração
                        somente para pedidos com a nova moeda configurada (Agrupamento por Moeda Nativa).
                        Garanta que a moeda selecionada reflecte o acordo real de budget anual do departamento.
                    </p>
                </div>
            </div>
        </div>
    );
}
