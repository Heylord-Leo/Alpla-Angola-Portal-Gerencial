import { useEffect, useState } from 'react';
import { AlertTriangle, Wallet } from 'lucide-react';
import { api } from '../../lib/api';
import type { SupplierCreditEligibilityDto } from '../../types';
import { CurrencyInput } from '../CurrencyInput';
import { formatCurrencyAO } from '../../lib/utils';

/**
 * "Utilizar crédito em conta corrente" — the P.O. wizard block shown BELOW "Condição de pagamento" (never a fourth
 * payment condition). Reads the server eligibility for THIS group (flag, account, max coverage, current hold) and
 * surfaces the decision to the host modal, which puts it on the register-po payload. The server re-validates
 * everything (responsible Buyer, flag, availability, normalisation); the block is presentation + capture only.
 */
export interface SupplierCreditDecision {
    /** First registration: the Buyer asked to use credit. */
    useCredit: boolean;
    /** Requested coverage (first registration or CHANGE). */
    amount: number | null;
    /** Correction with an existing hold: KEEP | CHANGE | REMOVE. */
    correctionAction: 'KEEP' | 'CHANGE' | 'REMOVE' | null;
}

export const EMPTY_CREDIT_DECISION: SupplierCreditDecision = { useCredit: false, amount: null, correctionAction: null };

interface Props {
    requestId: string;
    poGroupId: string;
    paymentCondition: string;
    /** Group total shown by the host; the server figure (eligibility.groupTotalAmount) wins when they differ. */
    totalAmount: number;
    currencyCode?: string | null;
    mode: 'register' | 'correct';
    value: SupplierCreditDecision;
    onChange: (next: SupplierCreditDecision, eligibility: SupplierCreditEligibilityDto | null) => void;
    disabled?: boolean;
}

const SUPPORTED_FALLBACK = ['POST_PAID', 'ADVANCE_FULL'];

export function computeBankPart(total: number, coverage: number | null): number {
    if (coverage == null || !isFinite(coverage)) return total;
    return Math.max(0, Math.round((total - coverage) * 100) / 100);
}

export function SupplierCreditCoverageBlock({ requestId, poGroupId, paymentCondition, totalAmount, currencyCode, mode, value, onChange, disabled }: Props) {
    const [eligibility, setEligibility] = useState<SupplierCreditEligibilityDto | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [amountText, setAmountText] = useState<string>(value.amount != null ? value.amount.toFixed(2) : '');

    useEffect(() => {
        let cancelled = false;
        if (!requestId || !poGroupId) { setEligibility(null); return; }
        setLoading(true); setError(null);
        api.requests.getCreditEligibility(requestId, poGroupId, paymentCondition || undefined)
            .then(e => {
                if (cancelled) return;
                setEligibility(e);
                // Correction with an existing hold: default decision KEEP, surfaced immediately so the submit carries it.
                if (mode === 'correct' && e.currentReservation && value.correctionAction == null) {
                    onChange({ useCredit: false, amount: e.currentReservation.amount, correctionAction: 'KEEP' }, e);
                    setAmountText(e.currentReservation.amount.toFixed(2));
                } else {
                    onChange(value, e);
                }
            })
            .catch(err => { if (!cancelled) { setEligibility(null); setError(err?.message || 'Não foi possível consultar o crédito em conta corrente.'); } })
            .finally(() => { if (!cancelled) setLoading(false); });
        return () => { cancelled = true; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [requestId, poGroupId, paymentCondition]);

    const total = eligibility?.groupTotalAmount || totalAmount;
    const ccy = eligibility?.currencyCode || currencyCode || '';
    const supported = (eligibility?.supportedPaymentConditions?.length ? eligibility.supportedPaymentConditions : SUPPORTED_FALLBACK).includes(paymentCondition);
    const current = eligibility?.currentReservation ?? null;
    const hasHold = mode === 'correct' && !!current && current.status === 'RESERVED';

    // Nothing to show: no account on this supplier/company/currency and no existing hold (the option simply does not appear).
    if (!loading && !error && eligibility && !eligibility.account && !current) return null;
    if (!paymentCondition && !current) return null;

    const setAmount = (txt: string) => {
        setAmountText(txt);
        const parsed = parseFloat(txt.replace(/\s/g, '').replace(',', '.'));
        onChange({ ...value, amount: isFinite(parsed) ? parsed : null }, eligibility);
    };

    const maxCoverage = eligibility ? Math.min(eligibility.maxCoverage, total) : total;
    const effectiveAmount = value.amount ?? (current?.amount ?? null);
    const bankPart = computeBankPart(total, (value.useCredit || value.correctionAction === 'KEEP' || value.correctionAction === 'CHANGE') ? effectiveAmount : null);
    const amountInvalid = (value.useCredit || value.correctionAction === 'CHANGE') && (value.amount == null || value.amount <= 0 || value.amount > maxCoverage + 0.001);

    const box: React.CSSProperties = { marginBottom: '24px', padding: '20px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: 'var(--radius-md)' };
    const label: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' };
    const row: React.CSSProperties = { display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', fontWeight: 700, padding: '4px 0' };
    const pill = (active: boolean): React.CSSProperties => ({
        padding: '8px 14px', borderRadius: 'var(--radius-sm)', fontWeight: 800, fontSize: '0.78rem', cursor: disabled ? 'not-allowed' : 'pointer', fontFamily: 'inherit',
        border: active ? '2px solid var(--color-primary)' : '1.5px solid var(--color-border)',
        backgroundColor: active ? 'rgba(var(--color-primary-rgb), 0.08)' : 'var(--color-bg-surface)', color: active ? 'var(--color-primary)' : 'var(--color-text-muted)',
    });

    return (
        <div style={box} data-testid="supplier-credit-block" data-tour="po-supplier-credit">
            <label style={label}><Wallet size={14} /> Utilizar crédito em conta corrente</label>
            {loading && <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>A consultar o saldo de crédito do fornecedor…</p>}
            {error && <p style={{ fontSize: '0.75rem', color: '#b91c1c', fontWeight: 600 }}>{error}</p>}

            {eligibility?.account && (
                <div style={{ marginBottom: '10px', fontSize: '0.78rem', color: 'var(--color-text-main)' }}>
                    <div style={row}><span style={{ color: 'var(--color-text-muted)' }}>Conta</span><span>{eligibility.account.accountNumber} · {eligibility.account.companyName} · {eligibility.account.currencyCode}</span></div>
                    <div style={row}><span style={{ color: 'var(--color-text-muted)' }}>Saldo</span><span>{formatCurrencyAO(eligibility.account.balance)} {ccy}</span></div>
                    <div style={row}><span style={{ color: 'var(--color-text-muted)' }}>Reservado</span><span>{formatCurrencyAO(eligibility.account.reserved)} {ccy}</span></div>
                    <div style={row}><span style={{ color: 'var(--color-text-muted)' }}>Disponível para esta P.O</span><span style={{ color: 'var(--color-primary)' }}>{formatCurrencyAO(eligibility.availableForGroup)} {ccy}</span></div>
                </div>
            )}

            {/* ── Correction: existing hold → Manter / Alterar / Remover ── */}
            {hasHold && current && (
                <div>
                    <p style={{ fontSize: '0.78rem', fontWeight: 600, marginBottom: '8px' }}>
                        Reserva atual: <strong>{formatCurrencyAO(current.amount)} {ccy}</strong> ({current.statusLabel}).
                    </p>
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                        {(['KEEP', 'CHANGE', 'REMOVE'] as const).map(a => {
                            const changeDisabled = a === 'CHANGE' && eligibility && !eligibility.enabled;
                            return (
                                <button key={a} type="button" disabled={disabled || !!changeDisabled} data-testid={`supplier-credit-${a.toLowerCase()}`}
                                    title={changeDisabled ? 'A alteração do valor de crédito não está ativa neste ambiente.' : undefined}
                                    onClick={() => onChange({ useCredit: false, amount: a === 'REMOVE' ? null : (a === 'KEEP' ? current.amount : value.amount ?? current.amount), correctionAction: a }, eligibility)}
                                    style={{ ...pill(value.correctionAction === a), opacity: changeDisabled ? 0.5 : 1 }}>
                                    {a === 'KEEP' ? 'Manter' : a === 'CHANGE' ? 'Alterar' : 'Remover'}
                                </button>
                            );
                        })}
                    </div>
                    {value.correctionAction === 'REMOVE' && (
                        <p style={{ marginTop: '8px', fontSize: '0.75rem', color: '#92400e', fontWeight: 600 }}>A reserva é libertada; a P.O passa a ser paga integralmente por banco.</p>
                    )}
                </div>
            )}

            {/* ── First registration (or a correction adding credit) ── */}
            {!hasHold && eligibility?.account && (
                <div>
                    {!eligibility.canReserve ? (
                        <p style={{ fontSize: '0.75rem', color: '#92400e', fontWeight: 600, display: 'flex', gap: '6px', alignItems: 'center' }}>
                            <AlertTriangle size={14} /> {eligibility.reasonMessage || 'Crédito em conta corrente indisponível para esta P.O.'}
                        </p>
                    ) : !supported ? (
                        <p style={{ fontSize: '0.75rem', color: '#92400e', fontWeight: 600 }}>Nesta fase o crédito em conta corrente aplica-se apenas a Pós-Pago e 100% Antecipado.</p>
                    ) : (
                        <label style={{ display: 'flex', alignItems: 'center', gap: '10px', fontSize: '0.85rem', fontWeight: 700, cursor: disabled ? 'not-allowed' : 'pointer' }}>
                            <input type="checkbox" checked={value.useCredit} disabled={disabled} data-testid="supplier-credit-use"
                                onChange={e => {
                                    const on = e.target.checked;
                                    const def = Math.min(maxCoverage, total);
                                    setAmountText(on ? def.toFixed(2) : '');
                                    onChange({ useCredit: on, amount: on ? def : null, correctionAction: value.correctionAction }, eligibility);
                                }} />
                            Utilizar crédito em conta corrente nesta P.O
                        </label>
                    )}
                </div>
            )}

            {/* ── Amount (first registration, or CHANGE) ── */}
            {((value.useCredit && !hasHold) || value.correctionAction === 'CHANGE') && eligibility?.account && (
                <div style={{ marginTop: '12px' }}>
                    <label style={{ ...label, marginBottom: '6px' }}>Valor de crédito a utilizar (máx. {formatCurrencyAO(maxCoverage)} {ccy})</label>
                    <CurrencyInput value={amountText} onChange={setAmount} placeholder="0,00" disabled={disabled} data-testid="supplier-credit-amount"
                        style={{ width: '100%', padding: '10px 12px', border: amountInvalid ? '2px solid #ef4444' : '2px solid var(--color-border)', borderRadius: 'var(--radius-sm)', fontWeight: 700, fontFamily: 'inherit' }} />
                    {amountInvalid && <p style={{ fontSize: '0.72rem', color: '#b91c1c', fontWeight: 600, marginTop: '4px' }}>Indique um valor entre 0,01 e {formatCurrencyAO(maxCoverage)} {ccy}.</p>}
                </div>
            )}

            {/* ── Resulting split ── */}
            {(value.useCredit || value.correctionAction === 'KEEP' || value.correctionAction === 'CHANGE') && effectiveAmount != null && !amountInvalid && (
                <div style={{ marginTop: '12px', padding: '10px 12px', borderRadius: 'var(--radius-sm)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', fontSize: '0.78rem', fontWeight: 700 }} data-testid="supplier-credit-split">
                    <div style={row}><span>Valor da P.O</span><span>{formatCurrencyAO(total)} {ccy}</span></div>
                    <div style={row}><span>Crédito em conta corrente</span><span>{formatCurrencyAO(effectiveAmount)} {ccy}</span></div>
                    <div style={row}><span>{paymentCondition === 'ADVANCE_FULL' ? 'Adiantamento bancário' : 'Pagamento bancário'}</span><span style={{ color: bankPart === 0 ? '#16a34a' : 'var(--color-primary)' }}>{formatCurrencyAO(bankPart)} {ccy}{bankPart === 0 ? ' — cobertura integral' : ''}</span></div>
                </div>
            )}
        </div>
    );
}
