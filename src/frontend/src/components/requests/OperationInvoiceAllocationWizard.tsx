import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, RefreshCw } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { MoneyInput } from '../ui/MoneyInput';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import {
    aggregateStatusPresentation,
    documentStatusPresentation,
    isInvoiceAwaitingDecision,
    coverageView,
    isGroupAllocatable,
    formatMoney,
    mapOperationInvoiceError
} from '../../lib/operationInvoiceView';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';
import type {
    OperationInvoiceDto,
    OperationInvoiceObligationsDto,
    OperationInvoiceObligationDto,
    OperationInvoiceAllocationDto,
    SaveOperationInvoiceAllocationItemDto
} from '../../types/operationInvoice';

/**
 * Step 2 selectability of one group against THIS invoice (v2.228.3), mirroring the backend's
 * rule order for UX only — the backend remains authoritative. Priority: ClosedShort (hard, every
 * actor) → supplier identity → currency identity → Buyer on a fully covered group. A fully
 * covered, non-short-closed group stays SELECTABLE for Finance/SysAdmin as the divergence path.
 */
function groupSelectability(
    group: OperationInvoiceObligationDto,
    invoice: OperationInvoiceDto,
    isFinanceActor: boolean
): { disabled: boolean; note: string | null; warning: string | null } {
    if (group.closedShort) {
        return { disabled: true, note: 'Grupo encerrado com saldo aceite', warning: null };
    }
    if (group.supplierId != null && invoice.supplierId != null && group.supplierId !== invoice.supplierId) {
        return { disabled: true, note: 'Fornecedor diferente do da fatura', warning: null };
    }
    if (group.currency && invoice.currency &&
        group.currency.toUpperCase() !== invoice.currency.toUpperCase()) {
        return { disabled: true, note: 'Moeda diferente da da fatura', warning: null };
    }
    const fullyCovered = group.expectedAmount != null && group.expectedAmount > 0 &&
        group.remainingAmount != null && group.remainingAmount <= group.appliedTolerance;
    if (fullyCovered) {
        return isFinanceActor
            ? {
                disabled: false, note: null,
                warning: 'Grupo totalmente coberto — qualquer nova distribuição exigirá análise de divergência.'
            }
            : { disabled: true, note: 'Grupo totalmente coberto', warning: null };
    }
    return { disabled: false, note: null, warning: null };
}

interface AllocationWizardProps {
    requestId: string;
    invoice: OperationInvoiceDto;
    obligations: OperationInvoiceObligationsDto;
    /** Finance/SysAdmin: over-expected becomes a divergence CANDIDATE with mandatory notes. */
    isFinanceActor: boolean;
    onClose: () => void;
    onSaved: () => void;
}

interface DraftRow {
    groupId: string;
    selected: boolean;
    gross: string;
    notes: string;
}

const STEPS = ['Fatura', 'Grupos', 'Distribuição', 'Revisão', 'Confirmar'] as const;

/**
 * Release 4 Phase 3B — "Distribuir Fatura Final".
 *
 * The allocation PUT is a WHOLE-SET replacement: this wizard always submits the complete intended
 * set for the invoice, never an increment. Only backend-eligible groups are offered; the backend
 * stays authoritative if eligibility changes between load and submit. Nothing is silently capped —
 * the Buyer's over-expected attempt is a hard business error, and the Finance divergence candidate
 * demands its written justification here without EVER being labelled accepted (acceptance belongs
 * to validation).
 */
export function OperationInvoiceAllocationWizard({
    requestId, invoice, obligations, isFinanceActor, onClose, onSaved
}: AllocationWizardProps) {
    const readOnly = !isInvoiceAwaitingDecision(invoice.status);

    const [step, setStep] = useState(0);
    const [existing, setExisting] = useState<OperationInvoiceAllocationDto[] | null>(null);
    const [rows, setRows] = useState<DraftRow[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [errorGroupId, setErrorGroupId] = useState<string | null>(null);
    const [isConcurrency, setIsConcurrency] = useState(false);

    const eligibleGroups = useMemo(
        () => obligations.obligations.filter(isGroupAllocatable),
        [obligations]);

    const byGroup = useMemo(
        () => new Map(obligations.obligations.map(o => [o.groupId, o])),
        [obligations]);

    // ── Load the current allocation set and seed the draft ──
    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const current = await operationInvoiceApi.getAllocations(requestId, invoice.id);
                if (cancelled) return;
                setExisting(current);
                setRows(eligibleGroups.map(group => {
                    const row = current.find(a => a.requestPoGroupId === group.groupId);
                    return {
                        groupId: group.groupId,
                        selected: !!row,
                        gross: row ? String(row.allocatedGrossAmount) : '',
                        notes: row?.notes ?? ''
                    };
                }));
            } catch (err) {
                if (!cancelled) setError(mapOperationInvoiceError(err).message);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [requestId, invoice.id, eligibleGroups]);

    const parse = (v: string) => v.trim() === '' ? 0 : Number(v.replace(',', '.')) || 0;

    const selectedRows = rows.filter(r => r.selected);
    const allocatedTotal = selectedRows.reduce((sum, r) => sum + parse(r.gross), 0);
    const invoiceGross = invoice.grossAmount ?? 0;
    const invoiceRemaining = invoiceGross - allocatedTotal;
    const tolerance = Math.max(1, Math.abs(invoiceGross) * 0.001);

    /** Draft-time divergence candidates: selected rows pushing a group beyond its expected total. */
    const divergenceCandidates = selectedRows.filter(row => {
        const group = byGroup.get(row.groupId);
        if (!group || group.expectedAmount == null || group.expectedAmount <= 0) return false;
        const validated = group.validatedCoveredAmount;
        const groupTolerance = group.appliedTolerance;
        return validated + parse(row.gross) > group.expectedAmount + groupTolerance;
    });

    // Same rule as the backend validator (length + placeholder rejection), for UX only.
    const missingDivergenceNotes = isFinanceActor
        ? divergenceCandidates.filter(r => !validateReconciliationJustification(r.notes).isValid)
        : [];

    const buyerBlocked = !isFinanceActor && divergenceCandidates.length > 0;

    const updateRow = (groupId: string, patch: Partial<DraftRow>) => {
        setRows(rs => rs.map(r => r.groupId === groupId ? { ...r, ...patch } : r));
        setError(null);
        setErrorGroupId(null);
    };

    const submit = async () => {
        setSaving(true);
        setError(null);
        setErrorGroupId(null);
        setIsConcurrency(false);
        try {
            const payload: SaveOperationInvoiceAllocationItemDto[] = selectedRows
                .filter(r => parse(r.gross) > 0)
                .map(r => ({
                    requestPoGroupId: r.groupId,
                    allocatedNetAmount: 0,
                    allocatedTaxAmount: 0,
                    allocatedGrossAmount: parse(r.gross),
                    notes: r.notes.trim() || null
                }));

            await operationInvoiceApi.saveAllocations(requestId, invoice.id, {
                rowVersion: invoice.rowVersion ?? null,
                allocations: payload
            });
            onSaved();
        } catch (err) {
            const mapped = mapOperationInvoiceError(err);
            setError(mapped.message);
            setIsConcurrency(mapped.isConcurrency);
            const groupId = mapped.extensions['requestPoGroupId'];
            if (typeof groupId === 'string') {
                setErrorGroupId(groupId);
                setStep(2);   // bring the user back to the row the backend refused
            }
        } finally {
            setSaving(false);
        }
    };

    const invoiceStatus = documentStatusPresentation(invoice.status);

    // The divergence justification gate lives on the STEP, not only on the final confirm: an
    // invalid justification must never reach Revisão/Confirmar (backend stays authoritative).
    const nextDisabled =
        (step === 1 && selectedRows.length === 0 && !readOnly) ||
        (step === 2 && !readOnly &&
            (buyerBlocked || selectedRows.some(r => parse(r.gross) <= 0) || missingDivergenceNotes.length > 0));

    return (
        <ModalWrapper title={readOnly ? 'Distribuição da Fatura Final' : 'Distribuir Fatura Final'} onClose={onClose} width={760}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {/* ── Step header ── */}
                <div style={{ display: 'flex', gap: '4px' }}>
                    {STEPS.map((label, i) => (
                        <div key={label} style={{
                            flex: 1, textAlign: 'center', padding: '6px 4px', fontSize: '0.72rem', fontWeight: 800,
                            borderBottom: `3px solid ${i === step ? 'var(--color-primary)' : i < step ? '#86efac' : '#e2e8f0'}`,
                            color: i === step ? 'var(--color-primary)' : 'var(--color-text-muted)',
                            textTransform: 'uppercase'
                        }}>
                            {i + 1}. {label}
                        </div>
                    ))}
                </div>

                {loading ? (
                    <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)' }}>A carregar…</div>
                ) : (
                <>
                {/* ── Step 1: invoice context ── */}
                {step === 0 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <div style={{ fontWeight: 800, fontSize: '1rem' }}>
                            {invoice.supplierName || '—'} · {invoice.documentNumber || 'Sem número'}
                            {invoice.documentSeries ? ` (série ${invoice.documentSeries})` : ''}
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: '10px', fontSize: '0.85rem' }}>
                            <SummaryCell label="Total da fatura" value={formatMoney(invoiceGross, invoice.currency)} />
                            <SummaryCell label="Já distribuído" value={formatMoney(existing?.reduce((s, a) => s + a.allocatedGrossAmount, 0) ?? 0, invoice.currency)} />
                            <SummaryCell label="Estado" value={invoiceStatus.label} />
                        </div>
                        {readOnly && (
                            <div style={{ fontSize: '0.82rem', padding: '10px 12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px', color: '#475569', fontWeight: 600 }}>
                                Esta fatura já foi decidida — a distribuição é apenas de leitura.
                                {invoice.status === 'VALIDATED' && ' Os valores distribuídos contam como cobertura validada.'}
                            </div>
                        )}
                        {!readOnly && (
                            <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                Os valores distribuídos ficam <b>em validação</b> até à decisão do Financeiro —
                                só a validação os torna cobertura efetiva.
                            </div>
                        )}
                    </div>
                )}

                {/* ── Step 2: eligible groups ── */}
                {step === 1 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {eligibleGroups.length === 0 && (
                            <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                Nenhum grupo elegível para receber esta fatura.
                            </div>
                        )}
                        {eligibleGroups.map(group => {
                            const row = rows.find(r => r.groupId === group.groupId);
                            const view = coverageView(group);
                            const status = aggregateStatusPresentation(group.derivedStatus, group.closedShort);
                            const selectability = groupSelectability(group, invoice, isFinanceActor);
                            // A group that already carries an allocation of THIS invoice stays
                            // editable (removing it is part of the replace-set); the disable
                            // applies to NEW selection only.
                            const checkboxDisabled = selectability.disabled && !(row?.selected ?? false);
                            return (
                                <label key={group.groupId} style={{
                                    display: 'flex', alignItems: 'flex-start', gap: '10px', padding: '10px 12px',
                                    border: `1px solid ${row?.selected ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                    borderRadius: '8px',
                                    cursor: readOnly || checkboxDisabled ? 'default' : 'pointer',
                                    opacity: checkboxDisabled ? 0.6 : 1,
                                    backgroundColor: row?.selected ? 'rgba(var(--color-primary-rgb), 0.03)' : '#fff'
                                }}>
                                    {!readOnly && (
                                        <input type="checkbox" checked={row?.selected ?? false}
                                               disabled={checkboxDisabled}
                                               onChange={e => updateRow(group.groupId, { selected: e.target.checked })}
                                               style={{ marginTop: '3px' }} />
                                    )}
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '3px', flex: 1 }}>
                                        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '8px', flexWrap: 'wrap' }}>
                                            <span style={{ fontWeight: 800 }}>{group.supplierName || '—'}</span>
                                            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>{status.label}</span>
                                        </div>
                                        <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                            {group.purchaseOrderNumber ? `P.O. ${group.purchaseOrderNumber}` : 'Sem P.O.'}
                                            {group.currency ? ` · ${group.currency}` : ''}
                                        </div>
                                        <div style={{ fontSize: '0.78rem', display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
                                            <span>Esperado: <b>{view.expectedLabel}</b></span>
                                            <span>Validado: <b style={{ color: '#15803d' }}>{view.validatedLabel}</b></span>
                                            <span>Em validação: <b style={{ color: '#1d4ed8' }}>{view.pendingLabel}</b></span>
                                            <span>Restante: <b>{view.remainingLabel}</b></span>
                                        </div>
                                        {selectability.note && (
                                            <span style={{ fontSize: '0.76rem', fontWeight: 700, color: '#64748b' }}>
                                                {selectability.note}
                                            </span>
                                        )}
                                        {selectability.warning && (
                                            <span style={{ fontSize: '0.76rem', fontWeight: 700, color: '#b45309' }}>
                                                {selectability.warning}
                                            </span>
                                        )}
                                    </div>
                                </label>
                            );
                        })}
                    </div>
                )}

                {/* ── Step 3: amounts ── */}
                {step === 2 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <div style={{
                            display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '10px', padding: '10px 12px',
                            backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px', fontSize: '0.85rem'
                        }}>
                            <SummaryCell label="Total da fatura" value={formatMoney(invoiceGross, invoice.currency)} />
                            <SummaryCell label="Distribuído" value={formatMoney(allocatedTotal, invoice.currency)} />
                            <SummaryCell label="Restante da fatura" value={formatMoney(invoiceRemaining, invoice.currency)}
                                         highlight={Math.abs(invoiceRemaining) <= tolerance ? '#15803d' : invoiceRemaining < 0 ? '#b91c1c' : undefined} />
                        </div>

                        {selectedRows.map(row => {
                            const group = byGroup.get(row.groupId);
                            if (!group) return null;
                            const view = coverageView(group);
                            const amount = parse(row.gross);
                            const isDivergent = divergenceCandidates.some(d => d.groupId === row.groupId);
                            const isBackendError = errorGroupId === row.groupId;
                            return (
                                <div key={row.groupId} style={{
                                    border: `1px solid ${isBackendError ? '#dc2626' : isDivergent ? '#f59e0b' : 'var(--color-border)'}`,
                                    borderRadius: '8px', padding: '10px 12px',
                                    display: 'flex', flexDirection: 'column', gap: '8px'
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '10px', flexWrap: 'wrap' }}>
                                        <span style={{ fontWeight: 800 }}>{group.supplierName || '—'}</span>
                                        <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                            Esperado {view.expectedLabel} · Validado {view.validatedLabel} · Restante {view.remainingLabel}
                                        </span>
                                    </div>
                                    {!readOnly ? (
                                        <MoneyInput
                                            value={row.gross}
                                            onChange={v => updateRow(row.groupId, { gross: v })}
                                            placeholder="Valor a distribuir"
                                            style={{
                                                padding: '8px 10px', border: '1px solid var(--color-border)',
                                                borderRadius: '8px', fontSize: '0.9rem', fontWeight: 700, width: '220px'
                                            }}
                                        />
                                    ) : (
                                        <span style={{ fontWeight: 800 }}>{formatMoney(amount, invoice.currency)}</span>
                                    )}

                                    {isDivergent && !isFinanceActor && (
                                        <div style={{ fontSize: '0.8rem', color: '#b91c1c', fontWeight: 700, display: 'flex', gap: '6px', alignItems: 'center' }}>
                                            <AlertTriangle size={14} />
                                            A distribuição excede o valor esperado deste grupo. Apenas o Financeiro
                                            pode registar uma divergência acima do esperado.
                                        </div>
                                    )}

                                    {isDivergent && isFinanceActor && (
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '8px', padding: '8px 10px' }}>
                                            <span style={{ fontSize: '0.8rem', color: '#92400e', fontWeight: 700 }}>
                                                Acima do valor esperado — candidato a divergência. Isto NÃO significa que
                                                a divergência está aceite: a fatura continua em validação e o Financeiro
                                                decide explicitamente a divergência ao validar.
                                            </span>
                                            <textarea
                                                value={row.notes}
                                                onChange={e => updateRow(row.groupId, { notes: e.target.value })}
                                                placeholder="Justificativa da divergência (mínimo 20 caracteres significativos)"
                                                disabled={readOnly}
                                                style={{
                                                    padding: '8px 10px', border: '1px solid #fde68a', borderRadius: '8px',
                                                    fontSize: '0.85rem', minHeight: '52px', resize: 'vertical'
                                                }}
                                            />
                                        </div>
                                    )}
                                </div>
                            );
                        })}

                        {buyerBlocked && (
                            <div style={{ fontSize: '0.82rem', color: '#b91c1c', fontWeight: 700 }}>
                                Corrija os valores acima do esperado antes de continuar.
                            </div>
                        )}
                    </div>
                )}

                {/* ── Step 4: review ── */}
                {step === 3 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                            <thead>
                                <tr style={{ borderBottom: '2px solid var(--color-border)', textAlign: 'left' }}>
                                    <th style={{ padding: '6px 8px' }}>Grupo</th>
                                    <th style={{ padding: '6px 8px', textAlign: 'right' }}>Valor</th>
                                    <th style={{ padding: '6px 8px' }}>Observações</th>
                                </tr>
                            </thead>
                            <tbody>
                                {selectedRows.map(row => {
                                    const group = byGroup.get(row.groupId);
                                    const isDivergent = divergenceCandidates.some(d => d.groupId === row.groupId);
                                    return (
                                        <tr key={row.groupId} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                            <td style={{ padding: '6px 8px', fontWeight: 700 }}>
                                                {group?.supplierName || row.groupId}
                                                {isDivergent && (
                                                    <span style={{ marginLeft: '6px', fontSize: '0.7rem', fontWeight: 800, color: '#b45309', backgroundColor: '#fffbeb', border: '1px solid #fde68a', padding: '2px 6px', borderRadius: '10px' }}>
                                                        Candidato a divergência
                                                    </span>
                                                )}
                                            </td>
                                            <td style={{ padding: '6px 8px', textAlign: 'right', fontWeight: 800 }}>
                                                {formatMoney(parse(row.gross), invoice.currency)}
                                            </td>
                                            <td style={{ padding: '6px 8px', color: 'var(--color-text-muted)' }}>{row.notes || '—'}</td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                        <div style={{ display: 'flex', gap: '16px', fontSize: '0.88rem', fontWeight: 700 }}>
                            <span>Distribuído: {formatMoney(allocatedTotal, invoice.currency)}</span>
                            <span style={{ color: Math.abs(invoiceRemaining) <= tolerance ? '#15803d' : '#b45309' }}>
                                Restante da fatura: {formatMoney(invoiceRemaining, invoice.currency)}
                            </span>
                        </div>
                        {Math.abs(invoiceRemaining) > tolerance && (
                            <div style={{ fontSize: '0.8rem', color: '#92400e', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '8px', padding: '8px 10px', fontWeight: 600 }}>
                                A fatura ainda não está totalmente distribuída. Pode guardar como rascunho,
                                mas a validação do Financeiro exigirá a distribuição completa.
                            </div>
                        )}
                    </div>
                )}

                {/* ── Step 5: confirm ── */}
                {step === 4 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', fontSize: '0.88rem' }}>
                        <div>
                            A distribuição de <b>{formatMoney(allocatedTotal, invoice.currency)}</b> por{' '}
                            <b>{selectedRows.length} grupo(s)</b> substitui integralmente a distribuição atual desta fatura.
                        </div>
                        {divergenceCandidates.length > 0 && (
                            <div style={{ fontSize: '0.82rem', color: '#92400e', fontWeight: 600 }}>
                                {divergenceCandidates.length} grupo(s) acima do esperado seguem como candidatos a
                                divergência — a decisão pertence à validação.
                            </div>
                        )}
                    </div>
                )}
                </>
                )}

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

                {/* ── Navigation ── */}
                <div style={{ display: 'flex', justifyContent: 'space-between', gap: '10px' }}>
                    <button
                        onClick={() => step === 0 ? onClose() : setStep(s => s - 1)}
                        disabled={saving}
                        style={{ padding: '9px 16px', border: '1px solid var(--color-border)', backgroundColor: '#fff', borderRadius: '8px', fontWeight: 700, cursor: 'pointer' }}
                    >
                        {step === 0 ? 'Fechar' : 'Voltar'}
                    </button>
                    {step < STEPS.length - 1 ? (
                        <button
                            onClick={() => setStep(s => s + 1)}
                            disabled={nextDisabled || loading}
                            style={{
                                padding: '9px 18px', border: 'none', backgroundColor: 'var(--color-primary)', color: '#fff',
                                borderRadius: '8px', fontWeight: 800, cursor: 'pointer', opacity: nextDisabled || loading ? 0.5 : 1
                            }}
                        >
                            Avançar
                        </button>
                    ) : !readOnly && (
                        <button
                            onClick={() => void submit()}
                            disabled={saving || buyerBlocked || missingDivergenceNotes.length > 0}
                            style={{
                                padding: '9px 18px', border: 'none', backgroundColor: '#15803d', color: '#fff',
                                borderRadius: '8px', fontWeight: 800, cursor: 'pointer',
                                opacity: saving || buyerBlocked || missingDivergenceNotes.length > 0 ? 0.6 : 1
                            }}
                        >
                            {saving ? 'A guardar…' : 'Confirmar Distribuição'}
                        </button>
                    )}
                </div>
            </div>
        </ModalWrapper>
    );
}

function SummaryCell({ label, value, highlight }: { label: string; value: string; highlight?: string }) {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
            <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>{label}</span>
            <span style={{ fontWeight: 800, color: highlight ?? 'var(--color-text-main)' }}>{value}</span>
        </div>
    );
}
