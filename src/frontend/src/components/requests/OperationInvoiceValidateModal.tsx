import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, RefreshCw, ShieldCheck } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { formatMoney, mapOperationInvoiceError } from '../../lib/operationInvoiceView';
import type {
    OperationInvoiceDto,
    OperationInvoiceObligationsDto,
    OperationInvoiceAllocationDto,
    OperationInvoiceDivergenceAcceptanceDto
} from '../../types/operationInvoice';

interface ValidateModalProps {
    requestId: string;
    invoice: OperationInvoiceDto;
    obligations: OperationInvoiceObligationsDto;
    onClose: () => void;
    onDecided: () => void;
}

interface DivergenceRow {
    groupId: string;
    supplierName: string;
    currency: string | null;
    expected: number;
    validatedBefore: number;
    allocated: number;
    resulting: number;
    variance: number;
    tolerance: number;
    draftNotes: string | null;
    /** NEVER pre-selected — the explicit Finance decision. */
    accepted: boolean;
    justification: string;
}

/**
 * Release 4 Phase 3B — "Validar Fatura".
 *
 * The review before the Finance decision: gross vs allocated completeness, the groups affected,
 * and — for any group the validation would push beyond its expected total — the EXPLICIT
 * divergence decision. Acceptance is never inferred from clicking Validate, and no checkbox is
 * pre-selected. The backend remains authoritative on every rule.
 */
export function OperationInvoiceValidateModal({
    requestId, invoice, obligations, onClose, onDecided
}: ValidateModalProps) {
    const [allocations, setAllocations] = useState<OperationInvoiceAllocationDto[] | null>(null);
    const [divergences, setDivergences] = useState<DivergenceRow[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [isConcurrency, setIsConcurrency] = useState(false);

    const byGroup = useMemo(
        () => new Map(obligations.obligations.map(o => [o.groupId, o])),
        [obligations]);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const rows = await operationInvoiceApi.getAllocations(requestId, invoice.id);
                if (cancelled) return;
                setAllocations(rows);

                // Over-expected detection mirrors the backend rule for DISPLAY; the backend
                // recomputes it authoritatively at validation.
                setDivergences(rows.flatMap(a => {
                    const group = byGroup.get(a.requestPoGroupId);
                    if (!group || group.expectedAmount == null || group.expectedAmount <= 0) return [];
                    const validatedBefore = group.validatedCoveredAmount;
                    const resulting = validatedBefore + a.allocatedGrossAmount;
                    const variance = resulting - group.expectedAmount;
                    if (variance <= group.appliedTolerance) return [];
                    return [{
                        groupId: group.groupId,
                        supplierName: group.supplierName || '—',
                        currency: group.currency ?? invoice.currency ?? null,
                        expected: group.expectedAmount,
                        validatedBefore,
                        allocated: a.allocatedGrossAmount,
                        resulting,
                        variance,
                        tolerance: group.appliedTolerance,
                        draftNotes: a.notes ?? null,
                        accepted: false,
                        justification: a.notes ?? ''
                    }];
                }));
            } catch (err) {
                if (!cancelled) setError(mapOperationInvoiceError(err).message);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [requestId, invoice.id, invoice.currency, byGroup]);

    const allocatedTotal = allocations?.reduce((sum, a) => sum + a.allocatedGrossAmount, 0) ?? 0;
    const gross = invoice.grossAmount ?? 0;
    const tolerance = Math.max(1, Math.abs(gross) * 0.001);
    const complete = Math.abs(gross - allocatedTotal) <= tolerance;

    const pendingDecisions = divergences.filter(d => !d.accepted || d.justification.trim().length < 20);

    const submit = async () => {
        setSaving(true);
        setError(null);
        setIsConcurrency(false);
        try {
            const acceptances: OperationInvoiceDivergenceAcceptanceDto[] = divergences.map(d => ({
                requestPoGroupId: d.groupId,
                accepted: d.accepted,
                justification: d.justification.trim() || null
            }));
            await operationInvoiceApi.validate(requestId, invoice.id, {
                rowVersion: invoice.rowVersion ?? null,
                divergenceAcceptances: acceptances.length > 0 ? acceptances : null
            });
            onDecided();
        } catch (err) {
            const mapped = mapOperationInvoiceError(err);
            setError(mapped.message);
            setIsConcurrency(mapped.isConcurrency);
        } finally {
            setSaving(false);
        }
    };

    const groupsAffected = allocations?.map(a => byGroup.get(a.requestPoGroupId)?.supplierName || '—') ?? [];

    return (
        <ModalWrapper title="Validar Fatura" onClose={onClose} width={700}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                {loading ? (
                    <div style={{ padding: '20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>A carregar…</div>
                ) : (
                <>
                <div style={{ fontWeight: 800 }}>
                    {invoice.supplierName || '—'} · {invoice.documentNumber || 'Sem número'}
                    {invoice.documentSeries ? ` (série ${invoice.documentSeries})` : ''}
                </div>

                <div style={{
                    display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '10px',
                    padding: '10px 12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0',
                    borderRadius: '8px', fontSize: '0.85rem'
                }}>
                    <div><b>Total da fatura</b><br />{formatMoney(gross, invoice.currency)}</div>
                    <div><b>Total distribuído</b><br />{formatMoney(allocatedTotal, invoice.currency)}</div>
                    <div>
                        <b>Distribuição</b><br />
                        <span style={{ color: complete ? '#15803d' : '#b91c1c', fontWeight: 800 }}>
                            {complete ? 'Completa' : 'Incompleta'}
                        </span>
                    </div>
                </div>

                {groupsAffected.length > 0 && (
                    <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                        Grupos afetados: {groupsAffected.join(', ')}
                    </div>
                )}

                {!complete && (
                    <div style={{ fontSize: '0.84rem', color: '#b91c1c', backgroundColor: '#fef2f2', border: '1px solid #fca5a5', borderRadius: '8px', padding: '10px 12px', fontWeight: 700 }}>
                        A soma das distribuições não corresponde ao total da fatura. Complete a
                        distribuição antes de validar — a validação seria recusada pelo servidor.
                    </div>
                )}

                {/* ── The explicit divergence decision ── */}
                {divergences.map(d => (
                    <div key={d.groupId} style={{
                        border: '1px solid #fde68a', backgroundColor: '#fffbeb', borderRadius: '8px',
                        padding: '12px', display: 'flex', flexDirection: 'column', gap: '8px'
                    }}>
                        <div style={{ fontWeight: 800, color: '#92400e', display: 'flex', gap: '6px', alignItems: 'center' }}>
                            <AlertTriangle size={15} /> Divergência acima do esperado — {d.supplierName}
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '6px', fontSize: '0.8rem' }}>
                            <span>Esperado: <b>{formatMoney(d.expected, d.currency)}</b></span>
                            <span>Validado antes: <b>{formatMoney(d.validatedBefore, d.currency)}</b></span>
                            <span>Nesta fatura: <b>{formatMoney(d.allocated, d.currency)}</b></span>
                            <span>Total resultante: <b>{formatMoney(d.resulting, d.currency)}</b></span>
                            <span>Variação: <b style={{ color: '#b45309' }}>+{formatMoney(d.variance, d.currency)}</b></span>
                            <span>Tolerância: <b>{formatMoney(d.tolerance, d.currency)}</b></span>
                        </div>
                        {d.draftNotes && (
                            <div style={{ fontSize: '0.78rem', color: '#78350f' }}>
                                Justificativa registada na distribuição: “{d.draftNotes}”
                            </div>
                        )}
                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 800, fontSize: '0.85rem', color: '#92400e', cursor: 'pointer' }}>
                            <input
                                type="checkbox"
                                checked={d.accepted}
                                onChange={e => setDivergences(ds => ds.map(x =>
                                    x.groupId === d.groupId ? { ...x, accepted: e.target.checked } : x))}
                            />
                            ACEITAR DIVERGÊNCIA
                        </label>
                        <textarea
                            value={d.justification}
                            onChange={e => setDivergences(ds => ds.map(x =>
                                x.groupId === d.groupId ? { ...x, justification: e.target.value } : x))}
                            placeholder="Justificativa da aceitação (mínimo 20 caracteres significativos)"
                            style={{
                                padding: '8px 10px', border: '1px solid #fde68a', borderRadius: '8px',
                                fontSize: '0.85rem', minHeight: '52px', resize: 'vertical'
                            }}
                        />
                    </div>
                ))}

                {divergences.length > 0 && pendingDecisions.length > 0 && (
                    <div style={{ fontSize: '0.8rem', color: '#92400e', fontWeight: 700 }}>
                        A validação exige a aceitação explícita e a justificativa de cada divergência.
                    </div>
                )}

                <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontWeight: 600, display: 'flex', gap: '6px', alignItems: 'center' }}>
                    <ShieldCheck size={15} />
                    Ao validar, os valores distribuídos tornam-se cobertura validada dos grupos.
                </div>

                {error && (
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 12px',
                        backgroundColor: '#fef2f2', border: '1px solid #fca5a5', borderRadius: '8px',
                        color: '#b91c1c', fontSize: '0.85rem', fontWeight: 700
                    }}>
                        <AlertTriangle size={15} /> {error}
                        {isConcurrency && (
                            <button onClick={onClose} style={{
                                marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '4px',
                                border: '1px solid #b91c1c', backgroundColor: '#fff', color: '#b91c1c',
                                borderRadius: '6px', padding: '4px 10px', fontWeight: 700, cursor: 'pointer'
                            }}>
                                <RefreshCw size={13} /> Recarregar dados
                            </button>
                        )}
                    </div>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                    <button onClick={onClose} disabled={saving} style={{
                        padding: '9px 16px', border: '1px solid var(--color-border)', backgroundColor: '#fff',
                        borderRadius: '8px', fontWeight: 700, cursor: 'pointer'
                    }}>
                        Cancelar
                    </button>
                    <button
                        onClick={() => void submit()}
                        disabled={saving || !complete || pendingDecisions.length > 0}
                        style={{
                            padding: '9px 18px', border: 'none', backgroundColor: '#15803d', color: '#fff',
                            borderRadius: '8px', fontWeight: 800, cursor: 'pointer',
                            opacity: saving || !complete || pendingDecisions.length > 0 ? 0.55 : 1
                        }}
                    >
                        {saving ? 'A validar…' : 'Validar Fatura'}
                    </button>
                </div>
                </>
                )}
            </div>
        </ModalWrapper>
    );
}
