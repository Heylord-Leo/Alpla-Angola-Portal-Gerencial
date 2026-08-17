import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, ChevronDown, ChevronUp, Download, RefreshCw, AlertTriangle } from 'lucide-react';
import { api } from '../../../lib/api';
import { operationInvoiceApi } from '../../../lib/operationInvoiceApi';
import {
    mapOperationInvoiceError,
    formatUtcTimestampDate,
    blockingReasonText,
    completionChecklist,
    canOfferFiscalReceiptUpload,
    acceptedDivergence,
    formatMoney
} from '../../../lib/operationInvoiceView';
import type {
    CompletionReadinessDto,
    CompletionReadinessGroupDto,
    OperationInvoiceObligationsDto
} from '../../../types/operationInvoice';
import { FiscalReceiptModal } from '../../../components/requests/FiscalReceiptModal';

interface RequestCompletionSectionProps {
    requestId: string;
    /** Coverage capability (flags.postPaymentCompletionEnabled). Section renders nothing when off. */
    coverageEnabled: boolean;
    /** Phase 4 lifecycle flag (flags.completionLifecycleEnabled) — presentation honesty only;
     *  the read model carries the authoritative value too. */
    lifecycleEnabled: boolean;
    isFinance: boolean;
    isAdmin: boolean;
}

/**
 * Release 4 Phase 4D — "Conclusão do Pedido": the completion readiness of every PO group.
 *
 * Pure presentation of the completion-readiness read model (GroupCompletionProjector,
 * server-side). Nothing here re-derives a completion predicate, and there is deliberately NO
 * "Concluir Pedido" button: Phase 4 completion is automatic through the backend engine — this
 * section shows readiness, what is missing, and who must act. The only mutation it offers is
 * the Finance-only fiscal receipt registration (Phase 4B write surface).
 */
export function RequestCompletionSection({
    requestId, coverageEnabled, lifecycleEnabled, isFinance, isAdmin
}: RequestCompletionSectionProps) {
    const [readiness, setReadiness] = useState<CompletionReadinessDto | null>(null);
    const [obligations, setObligations] = useState<OperationInvoiceObligationsDto | null>(null);
    const [loading, setLoading] = useState(false);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [isOpen, setIsOpen] = useState(true);
    const [fiscalReceiptGroup, setFiscalReceiptGroup] = useState<CompletionReadinessGroupDto | null>(null);

    const canUploadFiscalReceipt = isFinance || isAdmin;

    const refresh = useCallback(async () => {
        if (!coverageEnabled) return;
        setLoading(true);
        setLoadError(null);
        try {
            // Readiness is the authority; obligations ride along ONLY for the Phase 3 financial
            // evidence (accepted divergence, coverage figures) joined by group id.
            const [readinessResult, obligationsResult] = await Promise.all([
                operationInvoiceApi.getCompletionReadiness(requestId),
                operationInvoiceApi.getObligations(requestId).catch(() => null)
            ]);
            setReadiness(readinessResult);
            setObligations(obligationsResult);
        } catch (error) {
            setLoadError(mapOperationInvoiceError(error).message);
        } finally {
            setLoading(false);
        }
    }, [requestId, coverageEnabled]);

    useEffect(() => { void refresh(); }, [refresh]);

    const obligationByGroup = useMemo(() => {
        const map = new Map<string, NonNullable<typeof obligations>['obligations'][number]>();
        for (const o of obligations?.obligations ?? []) map.set(o.groupId, o);
        return map;
    }, [obligations]);

    if (!coverageEnabled) return null;
    if (!loading && !loadError && (readiness?.groups.length ?? 0) === 0) return null;

    // The authoritative flag: prefer the read model's value (same source of truth as the
    // backend), with the ConfigController flag as the pre-load fallback.
    const lifecycleActive = readiness?.completionLifecycleEnabled ?? lifecycleEnabled;

    const headerState = !readiness
        ? null
        : readiness.isCompleted
            ? { label: 'Pedido Concluído', color: '#15803d', bg: '#f0fdf4', border: '#86efac' }
            : readiness.isCompletionReady
                ? lifecycleActive
                    ? { label: 'Pronto para concluir', color: '#15803d', bg: '#f0fdf4', border: '#86efac' }
                    : { label: 'Requisitos de conclusão satisfeitos', color: '#0369a1', bg: '#f0f9ff', border: '#7dd3fc' }
                : { label: 'Conclusão pendente', color: '#9a3412', bg: '#fff7ed', border: '#fdba74' };

    const stateIcon = (state: 'done' | 'pending' | 'not-applicable' | 'blocked') => {
        switch (state) {
            case 'done': return <span style={{ color: '#15803d', fontWeight: 800 }}>✓</span>;
            case 'pending': return <span style={{ color: 'var(--color-text-muted)' }}>○</span>;
            case 'not-applicable': return <span style={{ color: 'var(--color-text-muted)' }}>—</span>;
            case 'blocked': return <span style={{ color: '#b91c1c', fontWeight: 800 }}>⚠</span>;
        }
    };

    const stateText = (state: 'done' | 'pending' | 'not-applicable' | 'blocked') => {
        switch (state) {
            case 'done': return 'Concluído';
            case 'pending': return 'Pendente';
            case 'not-applicable': return 'Não aplicável';
            case 'blocked': return 'Correção/Bloqueio';
        }
    };

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-md, 8px)',
            boxShadow: 'var(--shadow-soft)',
            overflow: 'hidden'
        }}>
            <button
                type="button"
                onClick={() => setIsOpen(o => !o)}
                style={{
                    width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '12px 20px', backgroundColor: 'transparent', border: 'none',
                    borderBottom: isOpen ? '1px solid var(--color-border)' : 'none',
                    cursor: 'pointer', textAlign: 'left'
                }}
            >
                <span style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                    <CheckCircle2 size={18} color="var(--color-primary, #0f766e)" />
                    <span style={{ fontWeight: 800, fontSize: '0.95rem' }}>Conclusão do Pedido</span>
                    {headerState && (
                        <span style={{
                            fontSize: '0.75rem', fontWeight: 800, padding: '2px 10px', borderRadius: '999px',
                            color: headerState.color, backgroundColor: headerState.bg,
                            border: `1px solid ${headerState.border}`
                        }}>
                            {headerState.label}
                        </span>
                    )}
                    {readiness && !readiness.isCompleted && readiness.totalGroupCount > 1 && (
                        <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                            {readiness.completedGroupCount} de {readiness.totalGroupCount} grupos concluídos
                        </span>
                    )}
                </span>
                {isOpen ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
            </button>

            {isOpen && (
                <div style={{ padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <button
                            type="button"
                            onClick={() => void refresh()}
                            disabled={loading}
                            style={{
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                padding: '6px 10px', borderRadius: '8px', border: '1px solid var(--color-border)',
                                backgroundColor: 'transparent', cursor: 'pointer',
                                fontSize: '0.78rem', color: 'var(--color-text-muted)'
                            }}
                        >
                            <RefreshCw size={13} className={loading ? 'animate-spin' : undefined} />
                            Atualizar
                        </button>
                    </div>

                    {loadError && (
                        <div style={{
                            border: '1px solid #fca5a5', backgroundColor: '#fef2f2', borderRadius: '8px',
                            padding: '10px 12px', fontSize: '0.83rem', color: '#991b1b',
                            display: 'flex', gap: '8px', alignItems: 'flex-start'
                        }}>
                            <AlertTriangle size={15} style={{ flexShrink: 0, marginTop: '2px' }} />
                            {loadError}
                        </div>
                    )}

                    {readiness?.isCompleted && (
                        <div style={{ fontSize: '0.85rem', color: '#166534' }}>
                            Pedido concluído
                            {readiness.completedAtUtc
                                ? ` em ${formatUtcTimestampDate(readiness.completedAtUtc)}`
                                : ''} pelo ciclo de conclusão pós-pagamento.
                        </div>
                    )}

                    {readiness && readiness.isCompletionReady && !readiness.isCompleted && !lifecycleActive && (
                        <div style={{
                            fontSize: '0.83rem', color: 'var(--color-text-muted)',
                            backgroundColor: 'var(--color-bg-muted, rgba(0,0,0,0.04))',
                            borderRadius: '8px', padding: '10px 12px'
                        }}>
                            O ciclo automático de conclusão ainda não está ativo neste ambiente.
                        </div>
                    )}

                    {(readiness?.groups ?? []).map(group => {
                        const obligation = obligationByGroup.get(group.groupId);
                        const divergence = obligation ? acceptedDivergence(obligation) : null;
                        const checklist = completionChecklist(group);
                        const offerUpload = canUploadFiscalReceipt &&
                            !readiness?.isCompleted &&
                            canOfferFiscalReceiptUpload(group);

                        return (
                            <div key={group.groupId} style={{
                                border: '1px solid var(--color-border)', borderRadius: '10px',
                                padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: '10px'
                            }}>
                                {/* ── Card header: supplier, PO, evidence badges, completed stamp ── */}
                                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                                    <span style={{ fontWeight: 800, fontSize: '0.9rem' }}>
                                        {group.supplierName || 'Grupo P.O.'}
                                    </span>
                                    {group.purchaseOrderNumber && (
                                        <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                            P.O. {group.purchaseOrderNumber}
                                        </span>
                                    )}
                                    {group.plantName && (
                                        <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                            {group.plantName}
                                        </span>
                                    )}
                                    {group.complete && (
                                        <span style={{
                                            fontSize: '0.73rem', fontWeight: 800, padding: '2px 8px',
                                            borderRadius: '999px', color: '#15803d',
                                            backgroundColor: '#f0fdf4', border: '1px solid #86efac'
                                        }}>
                                            Grupo Concluído
                                        </span>
                                    )}
                                    {group.closedShort && (
                                        <span style={{
                                            fontSize: '0.73rem', fontWeight: 700, padding: '2px 8px',
                                            borderRadius: '999px', color: '#0369a1',
                                            backgroundColor: '#f0f9ff', border: '1px solid #7dd3fc'
                                        }}>
                                            Encerrado com Saldo Aceite
                                        </span>
                                    )}
                                    {divergence && (
                                        <span style={{
                                            fontSize: '0.73rem', fontWeight: 700, padding: '2px 8px',
                                            borderRadius: '999px', color: '#9a3412',
                                            backgroundColor: '#fff7ed', border: '1px solid #fdba74'
                                        }}>
                                            Divergência Aceite: +{formatMoney(divergence.variance, obligation?.currency)}
                                        </span>
                                    )}
                                </div>

                                {group.complete && group.completedAtUtc && (
                                    <div style={{ fontSize: '0.78rem', color: '#166534' }}>
                                        Concluído em {formatUtcTimestampDate(group.completedAtUtc)}
                                    </div>
                                )}

                                {/* ── Compact checklist (kept visible even when completed — auditability) ── */}
                                <div style={{
                                    display: 'grid',
                                    gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
                                    gap: '6px'
                                }}>
                                    {checklist.map(item => (
                                        <span key={item.key} title={stateText(item.state)} style={{
                                            display: 'inline-flex', alignItems: 'center', gap: '6px',
                                            fontSize: '0.82rem',
                                            color: item.state === 'not-applicable'
                                                ? 'var(--color-text-muted)' : 'var(--color-text)'
                                        }}>
                                            {stateIcon(item.state)} {item.label}
                                            {item.state === 'not-applicable' && (
                                                <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                                    (Não aplicável)
                                                </span>
                                            )}
                                        </span>
                                    ))}
                                </div>

                                {/* ── "O que falta" — blocking reasons with ownership ── */}
                                {!group.complete && group.blockingReasons.length > 0 && (
                                    <div style={{
                                        borderTop: '1px dashed var(--color-border)', paddingTop: '8px',
                                        display: 'flex', flexDirection: 'column', gap: '4px'
                                    }}>
                                        <span style={{
                                            fontSize: '0.72rem', fontWeight: 800, textTransform: 'uppercase',
                                            color: 'var(--color-text-muted)'
                                        }}>
                                            O que falta
                                        </span>
                                        {group.blockingReasons.map(reason => (
                                            <span key={reason.code} style={{ fontSize: '0.82rem' }}>
                                                {blockingReasonText(reason)}
                                            </span>
                                        ))}
                                        {!group.classified && (
                                            <span style={{ fontSize: '0.76rem', color: 'var(--color-text-muted)' }}>
                                                Este grupo foi criado antes do fluxo atual e ainda não possui
                                                classificação suficiente para conclusão automática.
                                            </span>
                                        )}
                                    </div>
                                )}

                                {/* ── Fiscal receipt: evidence when bound; Finance CTA when unlocked ── */}
                                {group.fiscalReceipt && (
                                    <div style={{
                                        display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap',
                                        fontSize: '0.8rem', color: 'var(--color-text-muted)'
                                    }}>
                                        <span>
                                            Recibo Fiscal: <b style={{ color: 'var(--color-text)' }}>
                                                {group.fiscalReceipt.fileName || 'documento'}
                                            </b>
                                            {' · '}{formatUtcTimestampDate(group.fiscalReceipt.uploadedAtUtc)}
                                            {group.fiscalReceipt.uploadedByName
                                                ? ` · ${group.fiscalReceipt.uploadedByName}` : ''}
                                        </span>
                                        <button
                                            type="button"
                                            onClick={() => void api.attachments.download(
                                                group.fiscalReceipt!.attachmentId,
                                                group.fiscalReceipt!.fileName || 'recibo-fiscal.pdf')}
                                            style={{
                                                display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                padding: '4px 8px', borderRadius: '6px',
                                                border: '1px solid var(--color-border)',
                                                backgroundColor: 'transparent', cursor: 'pointer',
                                                fontSize: '0.76rem'
                                            }}
                                        >
                                            <Download size={12} /> Abrir
                                        </button>
                                    </div>
                                )}

                                {offerUpload && (
                                    <div>
                                        <button
                                            type="button"
                                            onClick={() => setFiscalReceiptGroup(group)}
                                            style={{
                                                padding: '8px 14px', borderRadius: '8px', border: 'none',
                                                backgroundColor: '#0f766e', color: '#fff',
                                                cursor: 'pointer', fontWeight: 800, fontSize: '0.83rem'
                                            }}
                                        >
                                            Registrar Recibo Fiscal
                                        </button>
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            )}

            {fiscalReceiptGroup && (
                <FiscalReceiptModal
                    requestId={requestId}
                    group={fiscalReceiptGroup}
                    onClose={() => setFiscalReceiptGroup(null)}
                    onChanged={() => void refresh()}
                />
            )}
        </div>
    );
}
