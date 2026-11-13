import React, { useEffect, useState } from 'react';
import { Plus, RefreshCw, Wallet } from 'lucide-react';
import { api } from '../../lib/api';
import type { SupplierCreditAccountDto, SupplierCreditMovementDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';
import { CurrencyInput } from '../../components/CurrencyInput';

/**
 * "Créditos em conta corrente" on the supplier ficha. Balances per buying company × currency (D1), append-only
 * movements. Purchasing (Contracts) and System Administrator create accounts and record CREDIT_IN / ADJUSTMENT_IN /
 * ADJUSTMENT_OUT; every other ficha viewer (Finance, Local Manager, request-scoped Buyer) reads. Authorization is the
 * backend capability (`canManage` on the list response) — never re-derived from roles here.
 */
interface Props {
    supplierId: number;
    onToast: (message: string, type: 'success' | 'error') => void;
}

const ENTRY_TYPES: { code: string; label: string; sign: 1 | -1 }[] = [
    { code: 'CREDIT_IN', label: 'Crédito registado (a favor)', sign: 1 },
    { code: 'ADJUSTMENT_IN', label: 'Ajuste a favor', sign: 1 },
    { code: 'ADJUSTMENT_OUT', label: 'Ajuste a desfavor', sign: -1 },
];

const fmt = (n: number | null | undefined, ccy?: string) => `${formatCurrencyAO(n ?? 0)} ${ccy ?? ''}`.trim();
const fmtDate = (iso: string) => { const d = new Date(iso.endsWith('Z') ? iso : iso + 'Z'); return isNaN(d.getTime()) ? iso : d.toLocaleString('pt-AO', { timeZone: 'Africa/Luanda' }); };
// Monetary cells: right-aligned, tabular digits, never wrapped (DEV round 1 feedback: 10 000,00 / 11 274,23 broke across lines).
const moneyCell: React.CSSProperties = { padding: '6px', textAlign: 'right', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' };
const moneyCellSm: React.CSSProperties = { padding: '5px', textAlign: 'right', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' };

const SupplierCreditSection: React.FC<Props> = ({ supplierId, onToast }) => {
    const [accounts, setAccounts] = useState<SupplierCreditAccountDto[]>([]);
    const [canManage, setCanManage] = useState(false);
    const [loading, setLoading] = useState(true);
    const [forbidden, setForbidden] = useState(false);
    const [selected, setSelected] = useState<string | null>(null);
    const [movements, setMovements] = useState<SupplierCreditMovementDto[]>([]);
    const [movLoading, setMovLoading] = useState(false);
    const [companies, setCompanies] = useState<any[]>([]);
    const [currencies, setCurrencies] = useState<any[]>([]);
    const [showCreate, setShowCreate] = useState(false);
    const [createForm, setCreateForm] = useState({ companyId: '', currencyCode: 'AOA', notes: '' });
    const [showEntry, setShowEntry] = useState(false);
    const [entryForm, setEntryForm] = useState({ entryType: 'CREDIT_IN', amount: '', reference: '', reason: '', documentDate: '' });
    const [saving, setSaving] = useState(false);

    const load = async () => {
        setLoading(true);
        try {
            const res = await api.lookups.getSupplierCreditAccounts(supplierId);
            setAccounts(res.items || []);
            setCanManage(!!res.canManage);
            setForbidden(false);
            if (selected && !(res.items || []).some(a => a.id === selected)) setSelected(null);
        } catch (err: any) {
            if (String(err?.message || '').includes('403')) setForbidden(true);
            setAccounts([]);
        } finally { setLoading(false); }
    };

    useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [supplierId]);
    useEffect(() => {
        if (!canManage) return;
        api.lookups.getCompanies().then(setCompanies).catch(() => {});
        api.lookups.getCurrencies?.().then((c: any) => setCurrencies(c || [])).catch(() => {});
    }, [canManage]);

    const openMovements = async (accountId: string) => {
        if (selected === accountId) { setSelected(null); return; }
        setSelected(accountId); setMovLoading(true);
        try { setMovements(await api.lookups.getSupplierCreditMovements(supplierId, accountId)); }
        catch (err: any) { onToast(err?.message || 'Falha ao carregar movimentos.', 'error'); setMovements([]); }
        finally { setMovLoading(false); }
    };

    const createAccount = async () => {
        if (!createForm.companyId || !createForm.currencyCode) { onToast('Indique a empresa compradora e a moeda.', 'error'); return; }
        setSaving(true);
        try {
            await api.lookups.createSupplierCreditAccount(supplierId, { companyId: Number(createForm.companyId), currencyCode: createForm.currencyCode, notes: createForm.notes || undefined });
            onToast('Conta corrente de crédito criada.', 'success');
            setShowCreate(false); setCreateForm({ companyId: '', currencyCode: 'AOA', notes: '' });
            await load();
        } catch (err: any) { onToast(err?.message || 'Falha ao criar a conta.', 'error'); }
        finally { setSaving(false); }
    };

    const addEntry = async () => {
        if (!selected) return;
        const amount = parseFloat(entryForm.amount.replace(/\s/g, '').replace(',', '.'));
        if (!isFinite(amount) || amount <= 0) { onToast('Indique um valor superior a zero.', 'error'); return; }
        if (!entryForm.reference.trim()) { onToast('A referência do documento do fornecedor é obrigatória.', 'error'); return; }
        if (entryForm.reason.trim().length < 10) { onToast('A justificativa deve ter pelo menos 10 caracteres.', 'error'); return; }
        setSaving(true);
        try {
            await api.lookups.addSupplierCreditEntry(supplierId, selected, {
                entryType: entryForm.entryType, amount, reference: entryForm.reference.trim(), reason: entryForm.reason.trim(),
                documentDateUtc: entryForm.documentDate ? new Date(entryForm.documentDate).toISOString() : null,
                clientKey: `${selected}:${Date.now()}`,
            });
            onToast('Movimento registado.', 'success');
            setShowEntry(false); setEntryForm({ entryType: 'CREDIT_IN', amount: '', reference: '', reason: '', documentDate: '' });
            await load();
            setMovements(await api.lookups.getSupplierCreditMovements(supplierId, selected));
        } catch (err: any) { onToast(err?.message || 'Falha ao registar o movimento.', 'error'); }
        finally { setSaving(false); }
    };

    if (forbidden) return null;

    const input: React.CSSProperties = { width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', fontFamily: 'inherit', fontSize: '0.85rem' };
    const btn: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: 'var(--radius-sm)', border: 'none', backgroundColor: 'var(--color-primary)', color: '#fff', fontWeight: 700, fontSize: '0.8rem', cursor: 'pointer' };
    const ghost: React.CSSProperties = { ...btn, backgroundColor: 'transparent', color: 'var(--color-text-main)', border: '1px solid var(--color-border)' };
    const selectedAccount = accounts.find(a => a.id === selected) || null;

    return (
        <div className="ficha-section ficha-section--full" data-tour="supplier-credit-section">
            <h2 className="ficha-section-title">
                <Wallet size={18} /> Créditos em conta corrente
                <span className="ficha-doc-completeness">{accounts.length} conta(s)</span>
                <span style={{ marginLeft: 'auto', display: 'flex', gap: '8px' }}>
                    <button type="button" style={ghost} onClick={load} title="Atualizar"><RefreshCw size={14} /></button>
                    {canManage && <button type="button" style={btn} onClick={() => setShowCreate(v => !v)} data-testid="supplier-credit-create"><Plus size={14} /> Nova conta</button>}
                </span>
            </h2>
            <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 0 }}>
                Saldo por empresa compradora e moeda. Compras regista os créditos; o Comprador reserva-os ao registar a P.O; Finanças aplica-os na liquidação.
                O valor reservado não é um movimento — só a aplicação por Finanças debita o saldo.
            </p>

            {showCreate && canManage && (
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 2fr auto', gap: '8px', alignItems: 'end', marginBottom: '12px', padding: '12px', border: '1px dashed var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                    <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Empresa compradora
                        <select style={input} value={createForm.companyId} onChange={e => setCreateForm({ ...createForm, companyId: e.target.value })}>
                            <option value="">—</option>
                            {companies.map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
                        </select>
                    </label>
                    <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Moeda
                        <select style={input} value={createForm.currencyCode} onChange={e => setCreateForm({ ...createForm, currencyCode: e.target.value })}>
                            {(currencies.length ? currencies.map((c: any) => c.code) : ['AOA', 'USD', 'EUR', 'ZAR']).map((c: string) => <option key={c} value={c}>{c}</option>)}
                        </select>
                    </label>
                    <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Notas (opcional)
                        <input style={input} value={createForm.notes} onChange={e => setCreateForm({ ...createForm, notes: e.target.value })} />
                    </label>
                    <button type="button" style={btn} disabled={saving} onClick={createAccount}>Criar</button>
                </div>
            )}

            {loading ? <p style={{ fontSize: '0.8rem' }}>A carregar…</p> : accounts.length === 0 ? (
                <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>Este fornecedor não tem contas correntes de crédito.</p>
            ) : (
                // Monetary cells never wrap (DEV round 1: amounts broke across lines); the table scrolls horizontally instead.
                <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem' }} data-testid="supplier-credit-accounts">
                    <thead>
                        <tr style={{ textAlign: 'left', color: 'var(--color-text-muted)', fontSize: '0.7rem', textTransform: 'uppercase' }}>
                            <th style={{ padding: '6px' }}>Conta</th><th style={{ padding: '6px' }}>Empresa</th><th style={{ padding: '6px' }}>Moeda</th>
                            <th style={{ padding: '6px', textAlign: 'right' }}>Saldo</th><th style={{ padding: '6px', textAlign: 'right' }}>Reservado</th><th style={{ padding: '6px', textAlign: 'right' }}>Disponível</th><th style={{ padding: '6px' }}>Estado</th><th />
                        </tr>
                    </thead>
                    <tbody>
                        {accounts.map(a => (
                            <tr key={a.id} style={{ borderTop: '1px solid var(--color-border)', backgroundColor: selected === a.id ? 'rgba(var(--color-primary-rgb), 0.05)' : undefined }}>
                                <td style={{ padding: '6px', fontWeight: 700, whiteSpace: 'nowrap' }}>{a.accountNumber}</td>
                                <td style={{ padding: '6px' }}>{a.companyName}</td>
                                <td style={{ padding: '6px' }}>{a.currencyCode}</td>
                                <td style={{ ...moneyCell }}>{fmt(a.balance)}</td>
                                <td style={{ ...moneyCell }}>{fmt(a.reserved)}</td>
                                <td style={{ ...moneyCell, fontWeight: 800, color: 'var(--color-primary)' }}>{fmt(a.available)}</td>
                                <td style={{ padding: '6px', whiteSpace: 'nowrap' }}>{a.status === 'ACTIVE' ? 'Ativa' : 'Encerrada'}</td>
                                <td style={{ padding: '6px', textAlign: 'right', whiteSpace: 'nowrap' }}>
                                    <button type="button" style={ghost} onClick={() => openMovements(a.id)}>{selected === a.id ? 'Ocultar' : 'Movimentos'}</button>
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
                </div>
            )}

            {selected && selectedAccount && (
                <div style={{ marginTop: '12px', padding: '12px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }} data-testid="supplier-credit-movements">
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                        <strong style={{ fontSize: '0.85rem' }}>Movimentos — {selectedAccount.accountNumber} ({selectedAccount.companyName} · {selectedAccount.currencyCode})</strong>
                        {canManage && selectedAccount.status === 'ACTIVE' && <button type="button" style={btn} onClick={() => setShowEntry(v => !v)} data-testid="supplier-credit-add-entry"><Plus size={14} /> Registar movimento</button>}
                    </div>
                    {showEntry && canManage && (
                        <div style={{ display: 'grid', gridTemplateColumns: '1.3fr 1fr 1fr 1fr', gap: '8px', alignItems: 'end', marginBottom: '10px', padding: '10px', border: '1px dashed var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                            <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Tipo
                                <select style={input} value={entryForm.entryType} onChange={e => setEntryForm({ ...entryForm, entryType: e.target.value })}>
                                    {ENTRY_TYPES.map(t => <option key={t.code} value={t.code}>{t.label}</option>)}
                                </select>
                            </label>
                            <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Valor
                                <CurrencyInput value={entryForm.amount} onChange={(v: string) => setEntryForm({ ...entryForm, amount: v })} placeholder="0,00" style={input} />
                            </label>
                            <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Referência do documento *
                                <input style={input} value={entryForm.reference} onChange={e => setEntryForm({ ...entryForm, reference: e.target.value })} placeholder="Nota de crédito, acordo, e-mail…" />
                            </label>
                            <label style={{ fontSize: '0.72rem', fontWeight: 700 }}>Data do documento
                                <input type="date" style={input} value={entryForm.documentDate} onChange={e => setEntryForm({ ...entryForm, documentDate: e.target.value })} />
                            </label>
                            <label style={{ fontSize: '0.72rem', fontWeight: 700, gridColumn: '1 / span 3' }}>Justificativa * (mín. 10 caracteres)
                                <input style={input} value={entryForm.reason} onChange={e => setEntryForm({ ...entryForm, reason: e.target.value })} />
                            </label>
                            <button type="button" style={btn} disabled={saving} onClick={addEntry}>Registar</button>
                        </div>
                    )}
                    {movLoading ? <p style={{ fontSize: '0.8rem' }}>A carregar…</p> : movements.length === 0 ? (
                        <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>Sem movimentos.</p>
                    ) : (
                        <div style={{ overflowX: 'auto' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.78rem' }} data-testid="supplier-credit-movements-table">
                            <thead>
                                <tr style={{ textAlign: 'left', color: 'var(--color-text-muted)', fontSize: '0.68rem', textTransform: 'uppercase' }}>
                                    <th style={{ padding: '5px' }}>Data</th><th style={{ padding: '5px' }}>Tipo</th><th style={{ padding: '5px', textAlign: 'right' }}>Valor</th><th style={{ padding: '5px', textAlign: 'right' }}>Reserva</th><th style={{ padding: '5px', textAlign: 'right' }}>Saldo após</th><th style={{ padding: '5px' }}>Pedido / P.O</th><th style={{ padding: '5px', minWidth: '180px' }}>Referência / motivo</th><th style={{ padding: '5px' }}>Por</th>
                                </tr>
                            </thead>
                            <tbody>
                                {movements.map(m => (
                                    <tr key={`${m.kind}-${m.id}`} style={{ borderTop: '1px solid var(--color-border)', opacity: m.kind === 'RESERVATION' ? 0.85 : 1, verticalAlign: 'top' }}>
                                        <td style={{ padding: '5px', whiteSpace: 'nowrap' }}>{fmtDate(m.occurredAtUtc)}</td>
                                        <td style={{ padding: '5px', whiteSpace: 'nowrap' }}>{m.typeLabel}{m.stage ? ` (${m.stage})` : ''}</td>
                                        {/* Amounts stay on one line; the "aplicado" detail of a hold goes on its own line under the hold. */}
                                        <td style={{ ...moneyCellSm, fontWeight: 700, color: (m.signedAmount ?? 0) < 0 ? '#b91c1c' : (m.signedAmount ?? 0) > 0 ? '#15803d' : undefined }}>{m.signedAmount != null ? fmt(m.signedAmount) : '—'}</td>
                                        <td style={{ ...moneyCellSm }}>
                                            {m.holdAmount != null ? fmt(m.holdAmount) : '—'}
                                            {m.appliedAmount ? <div style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)' }}>aplicado {fmt(m.appliedAmount)}</div> : null}
                                        </td>
                                        <td style={{ ...moneyCellSm }}>{m.balanceAfter != null ? fmt(m.balanceAfter) : '—'}</td>
                                        <td style={{ padding: '5px' }}>
                                            {m.requestId ? <a href={`/requests/${m.requestId}`} style={{ color: 'var(--color-primary)', fontWeight: 700, whiteSpace: 'nowrap' }}>{m.requestNumber || 'Pedido'}</a> : '—'}
                                            {m.purchaseOrderNumber ? <div style={{ whiteSpace: 'nowrap' }}>{m.purchaseOrderNumber}</div> : null}
                                            {m.plantName ? <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{m.plantName}</div> : null}
                                        </td>
                                        <td style={{ padding: '5px', overflowWrap: 'anywhere' }}>{[m.reference, m.reason].filter(Boolean).join(' — ')}</td>
                                        <td style={{ padding: '5px', whiteSpace: 'nowrap' }}>{m.actorName || '—'}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
};

export default SupplierCreditSection;
