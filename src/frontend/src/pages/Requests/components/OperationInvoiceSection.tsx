import { useCallback, useEffect, useMemo, useState } from 'react';
import { FileText, Plus, RefreshCw, AlertTriangle } from 'lucide-react';
import { KebabMenu } from '../../../components/ui/KebabMenu';
import { api } from '../../../lib/api';
import { operationInvoiceApi } from '../../../lib/operationInvoiceApi';
import {
    aggregateStatusPresentation,
    documentStatusPresentation,
    isInvoiceAwaitingDecision,
    isInvoiceEditable,
    formatMoney,
    coverageView,
    isShortCloseProposable,
    mapOperationInvoiceError
} from '../../../lib/operationInvoiceView';
import type {
    OperationInvoiceDto,
    OperationInvoiceObligationsDto,
    OperationInvoiceObligationDto,
    OperationInvoiceShortCloseDto
} from '../../../types/operationInvoice';
import { OperationInvoiceRegisterModal, RegisterModalMode } from '../../../components/requests/OperationInvoiceRegisterModal';
import { OperationInvoiceAllocationWizard } from '../../../components/requests/OperationInvoiceAllocationWizard';
import { OperationInvoiceValidateModal } from '../../../components/requests/OperationInvoiceValidateModal';
import { OperationInvoiceLifecycleModal, LifecycleAction } from '../../../components/requests/OperationInvoiceLifecycleModal';
import { OperationInvoiceShortCloseModal } from '../../../components/requests/OperationInvoiceShortCloseModal';

interface OperationInvoiceSectionProps {
    requestId: string;
    /** Coverage capability discovery (flags.postPaymentCompletionEnabled). Renders nothing when off. */
    coverageEnabled: boolean;
    isFinance: boolean;
    isBuyer: boolean;
    isAdmin: boolean;
    currentUserId: string | null;
}

/**
 * Release 4 Phase 3B — the Operation Invoice ("Fatura Final") workspace of one request.
 *
 * One authoritative read model: group coverage comes EXCLUSIVELY from the obligations endpoint;
 * the invoice list from the operation-invoices endpoint. Nothing here recomputes coverage.
 *
 * Rendered in the request detail (and therefore in the Finance drawer, which hosts the same
 * detail). The completion lifecycle (Phase 4) is deliberately absent — its flag being off is the
 * intended Phase 3B state, not a warning.
 */
export function OperationInvoiceSection({
    requestId, coverageEnabled, isFinance, isBuyer, isAdmin, currentUserId
}: OperationInvoiceSectionProps) {
    const canWrite = isFinance || isBuyer || isAdmin;
    const canDecide = isFinance || isAdmin;

    const [obligations, setObligations] = useState<OperationInvoiceObligationsDto | null>(null);
    const [invoices, setInvoices] = useState<OperationInvoiceDto[]>([]);
    const [shortClosesByGroup, setShortClosesByGroup] = useState<Record<string, OperationInvoiceShortCloseDto[]>>({});
    const [loading, setLoading] = useState(false);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [isOpen, setIsOpen] = useState(true);

    // Modals
    const [registerModal, setRegisterModal] = useState<{ mode: RegisterModalMode; invoice: OperationInvoiceDto | null } | null>(null);
    const [allocationInvoice, setAllocationInvoice] = useState<OperationInvoiceDto | null>(null);
    const [validateInvoice, setValidateInvoice] = useState<OperationInvoiceDto | null>(null);
    const [lifecycleModal, setLifecycleModal] = useState<{ action: LifecycleAction; invoice: OperationInvoiceDto } | null>(null);
    const [shortCloseGroup, setShortCloseGroup] = useState<OperationInvoiceObligationDto | null>(null);

    const refresh = useCallback(async () => {
        if (!coverageEnabled) return;
        setLoading(true);
        setLoadError(null);
        try {
            const [obligationsResult, invoicesResult] = await Promise.all([
                operationInvoiceApi.getObligations(requestId),
                operationInvoiceApi.list(requestId)
            ]);
            setObligations(obligationsResult);
            setInvoices(invoicesResult);

            // Short-close context, loaded only for groups where it can matter (active or history).
            const relevant = obligationsResult.obligations.filter(o =>
                o.closedShort || isShortCloseProposable(o));
            const entries = await Promise.all(relevant.map(async o => {
                try {
                    return [o.groupId, await operationInvoiceApi.listShortCloses(requestId, o.groupId)] as const;
                } catch {
                    return [o.groupId, []] as const;
                }
            }));
            setShortClosesByGroup(Object.fromEntries(entries));
        } catch (error) {
            setLoadError(mapOperationInvoiceError(error).message);
        } finally {
            setLoading(false);
        }
    }, [requestId, coverageEnabled]);

    useEffect(() => { void refresh(); }, [refresh]);

    const relevantObligations = useMemo(
        () => (obligations?.obligations ?? []).filter(o => o.requiresOperationInvoice),
        [obligations]);

    // Groups can exist without owing an invoice; requests can predate groups entirely. The section
    // only renders when the capability is on AND there is something to say.
    if (!coverageEnabled) return null;
    if (!loading && !loadError && relevantObligations.length === 0 && invoices.length === 0) return null;

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
                <span style={{
                    fontSize: '0.8rem', fontWeight: 900, color: 'var(--color-primary)',
                    textTransform: 'uppercase', letterSpacing: '0.08em',
                    display: 'flex', alignItems: 'center', gap: '10px'
                }}>
                    <FileText size={16} /> Fatura Final — Cobertura
                </span>
                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                    {relevantObligations.length > 0 && `${relevantObligations.length} grupo(s)`}
                    {invoices.length > 0 && ` · ${invoices.length} fatura(s)`}
                </span>
            </button>

            {isOpen && (
                <div style={{ padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    {loadError && (
                        <div style={{
                            display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 12px',
                            backgroundColor: '#fef2f2', border: '1px solid #fca5a5', borderRadius: '8px',
                            color: '#b91c1c', fontSize: '0.85rem', fontWeight: 600
                        }}>
                            <AlertTriangle size={16} /> {loadError}
                            <button onClick={() => void refresh()} style={{
                                marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '4px',
                                border: '1px solid #b91c1c', backgroundColor: '#fff', color: '#b91c1c',
                                borderRadius: '6px', padding: '4px 10px', fontWeight: 700, cursor: 'pointer'
                            }}>
                                <RefreshCw size={13} /> Recarregar dados
                            </button>
                        </div>
                    )}

                    {/* ── Coverage per group (authoritative: obligations endpoint) ── */}
                    {relevantObligations.map(obligation => (
                        <GroupCoverageCard
                            key={obligation.groupId}
                            obligation={obligation}
                            shortCloses={shortClosesByGroup[obligation.groupId] ?? []}
                            canWrite={canWrite}
                            canDecide={canDecide}
                            currentUserId={currentUserId}
                            onProposeShortClose={() => setShortCloseGroup(obligation)}
                            onOpenShortClose={() => setShortCloseGroup(obligation)}
                        />
                    ))}

                    {/* ── Invoice list ── */}
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                        <span style={{ fontSize: '0.78rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                            Faturas Finais Registadas
                        </span>
                        {canWrite && (
                            <button
                                onClick={() => setRegisterModal({ mode: 'create', invoice: null })}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: '6px', padding: '7px 14px',
                                    backgroundColor: 'var(--color-primary)', color: '#fff', border: 'none',
                                    borderRadius: '8px', fontWeight: 800, fontSize: '0.8rem', cursor: 'pointer'
                                }}
                            >
                                <Plus size={15} /> Registrar Fatura Final
                            </button>
                        )}
                    </div>

                    {invoices.length === 0 && !loading && (
                        <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                            Nenhuma fatura final registada neste pedido.
                        </div>
                    )}

                    {invoices.map(invoice => (
                        <InvoiceCard
                            key={invoice.id}
                            invoice={invoice}
                            canWrite={canWrite}
                            canDecide={canDecide}
                            onEdit={() => setRegisterModal({ mode: 'edit', invoice })}
                            onAllocate={() => setAllocationInvoice(invoice)}
                            onValidate={() => setValidateInvoice(invoice)}
                            onLifecycle={(action) => setLifecycleModal({ action, invoice })}
                            onReplace={() => setRegisterModal({ mode: 'replace', invoice })}
                        />
                    ))}
                </div>
            )}

            {/* ── Modals ── */}
            {registerModal && (
                <OperationInvoiceRegisterModal
                    requestId={requestId}
                    mode={registerModal.mode}
                    invoice={registerModal.invoice}
                    onClose={() => setRegisterModal(null)}
                    onSaved={() => { setRegisterModal(null); void refresh(); }}
                />
            )}
            {allocationInvoice && obligations && (
                <OperationInvoiceAllocationWizard
                    requestId={requestId}
                    invoice={allocationInvoice}
                    obligations={obligations}
                    isFinanceActor={canDecide}
                    onClose={() => setAllocationInvoice(null)}
                    onSaved={() => { setAllocationInvoice(null); void refresh(); }}
                />
            )}
            {validateInvoice && obligations && (
                <OperationInvoiceValidateModal
                    requestId={requestId}
                    invoice={validateInvoice}
                    obligations={obligations}
                    onClose={() => setValidateInvoice(null)}
                    onDecided={() => { setValidateInvoice(null); void refresh(); }}
                />
            )}
            {lifecycleModal && (
                <OperationInvoiceLifecycleModal
                    requestId={requestId}
                    invoice={lifecycleModal.invoice}
                    action={lifecycleModal.action}
                    onClose={() => setLifecycleModal(null)}
                    onDone={() => { setLifecycleModal(null); void refresh(); }}
                />
            )}
            {shortCloseGroup && (
                <OperationInvoiceShortCloseModal
                    requestId={requestId}
                    obligation={shortCloseGroup}
                    shortCloses={shortClosesByGroup[shortCloseGroup.groupId] ?? []}
                    canWrite={canWrite}
                    canDecide={canDecide}
                    currentUserId={currentUserId}
                    onClose={() => setShortCloseGroup(null)}
                    onChanged={() => { setShortCloseGroup(null); void refresh(); }}
                />
            )}
        </div>
    );
}

// ── Group coverage card ─────────────────────────────────────────────────────────────────────

const SEVERITY_COLORS: Record<string, { bg: string; fg: string; border: string }> = {
    success: { bg: '#f0fdf4', fg: '#15803d', border: '#bbf7d0' },
    warning: { bg: '#fffbeb', fg: '#b45309', border: '#fde68a' },
    error: { bg: '#fef2f2', fg: '#b91c1c', border: '#fca5a5' },
    info: { bg: '#eff6ff', fg: '#1d4ed8', border: '#bfdbfe' },
    muted: { bg: '#f8fafc', fg: '#475569', border: '#e2e8f0' }
};

function StatusChip({ label, severity }: { label: string; severity: string }) {
    const colors = SEVERITY_COLORS[severity] ?? SEVERITY_COLORS.muted;
    return (
        <span style={{
            fontSize: '0.72rem', fontWeight: 800, padding: '3px 10px', borderRadius: '12px',
            backgroundColor: colors.bg, color: colors.fg, border: `1px solid ${colors.border}`,
            whiteSpace: 'nowrap'
        }}>
            {label}
        </span>
    );
}

function GroupCoverageCard({
    obligation, shortCloses, canWrite, canDecide, currentUserId, onProposeShortClose, onOpenShortClose
}: {
    obligation: OperationInvoiceObligationDto;
    shortCloses: OperationInvoiceShortCloseDto[];
    canWrite: boolean;
    canDecide: boolean;
    currentUserId: string | null;
    onProposeShortClose: () => void;
    onOpenShortClose: () => void;
}) {
    const view = coverageView(obligation);
    const status = aggregateStatusPresentation(obligation.derivedStatus, obligation.closedShort);
    const pendingProposal = shortCloses.find(c => c.status === 'PROPOSED');
    const approvedShortClose = shortCloses.find(c => c.status === 'APPROVED');

    return (
        <div style={{
            border: '1px solid var(--color-border)', borderRadius: '10px', padding: '14px 16px',
            display: 'flex', flexDirection: 'column', gap: '10px', backgroundColor: '#fff'
        }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '10px', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <span style={{ fontWeight: 800, fontSize: '0.95rem' }}>{obligation.supplierName || 'Fornecedor —'}</span>
                    <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                        {obligation.purchaseOrderNumber ? `P.O. ${obligation.purchaseOrderNumber}` : 'Sem P.O.'}
                        {obligation.currency ? ` · ${obligation.currency}` : ''}
                    </span>
                </div>
                <StatusChip label={status.label} severity={status.severity} />
            </div>

            {/* The five coverage numbers — VALIDADO and EM VALIDAÇÃO are never conflated. */}
            <div style={{
                display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: '8px',
                fontSize: '0.82rem'
            }}>
                <CoverageCell label="Esperado" value={view.expectedLabel} muted={!view.hasExpected} />
                <CoverageCell label="Validado" value={view.validatedLabel} highlight="#15803d" />
                <CoverageCell label="Em validação" value={view.pendingLabel} highlight="#1d4ed8" />
                <CoverageCell label="Restante" value={view.remainingLabel} />
                <CoverageCell label="Cobertura" value={view.percent != null ? `${view.percent.toLocaleString('pt-AO')}%` : '—'} />
            </div>

            {view.percent != null && (
                <div style={{ height: '6px', backgroundColor: '#e2e8f0', borderRadius: '3px', overflow: 'hidden' }}>
                    <div style={{
                        width: `${Math.min(100, Math.max(0, view.percent))}%`, height: '100%',
                        backgroundColor: view.percent >= 100 ? '#16a34a' : '#0ea5e9', transition: 'width 0.3s'
                    }} />
                </div>
            )}

            {!view.hasExpected && (
                <div style={{ fontSize: '0.78rem', color: '#92400e', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', padding: '6px 10px', fontWeight: 600 }}>
                    O valor esperado da fatura final ainda não foi definido para este grupo.
                    {canDecide && ' A ativação controlada (Administração) permite preparar os valores esperados.'}
                </div>
            )}

            {approvedShortClose && (
                <div style={{ fontSize: '0.78rem', color: '#166534', backgroundColor: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '6px', padding: '6px 10px', fontWeight: 600 }}>
                    Encerrado com saldo aceite de {formatMoney(approvedShortClose.remainingAmountAtProposal, obligation.currency)}
                    {approvedShortClose.decidedByName ? ` — aprovado por ${approvedShortClose.decidedByName}` : ''}.
                    <button onClick={onOpenShortClose} style={{ marginLeft: '8px', border: 'none', background: 'none', color: '#166534', fontWeight: 800, cursor: 'pointer', textDecoration: 'underline' }}>
                        Ver histórico
                    </button>
                </div>
            )}

            {pendingProposal && (
                <div style={{ fontSize: '0.78rem', color: '#9a3412', backgroundColor: '#fff7ed', border: '1px solid #fdba74', borderRadius: '6px', padding: '6px 10px', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                    Proposta de encerramento com saldo pendente
                    ({formatMoney(pendingProposal.remainingAmountAtProposal, obligation.currency)})
                    {pendingProposal.proposedByName ? ` — proposta por ${pendingProposal.proposedByName}` : ''}.
                    <button onClick={onOpenShortClose} style={{ border: 'none', background: 'none', color: '#9a3412', fontWeight: 800, cursor: 'pointer', textDecoration: 'underline' }}>
                        {canDecide && pendingProposal.proposedByUserId !== currentUserId
                            ? 'Decidir'
                            : pendingProposal.proposedByUserId === currentUserId ? 'Ver / Retirar Proposta' : 'Ver'}
                    </button>
                </div>
            )}

            {/* Allocations touching this group (group-side view of the same rows). */}
            {obligation.allocations.length > 0 && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    {obligation.allocations.map(a => (
                        <div key={a.allocationId} style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                            <FileText size={13} />
                            <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                                {a.invoiceDocumentNumber || 'Fatura'}{a.invoiceDocumentSeries ? ` ${a.invoiceDocumentSeries}` : ''}
                            </span>
                            <span>{formatMoney(a.allocatedGrossAmount, obligation.currency)}</span>
                            <StatusChip
                                label={a.isEffective ? 'Validado' : a.isPendingDecision ? 'Em validação' : documentStatusPresentation(a.invoiceStatus).label}
                                severity={a.isEffective ? 'success' : a.isPendingDecision ? 'info' : 'muted'}
                            />
                        </div>
                    ))}
                </div>
            )}

            {canWrite && !pendingProposal && !approvedShortClose && isShortCloseProposable(obligation) && (
                <div>
                    <button
                        onClick={onProposeShortClose}
                        style={{
                            padding: '6px 12px', border: '1px solid var(--color-border)', backgroundColor: '#fff',
                            color: 'var(--color-text-main)', borderRadius: '6px', fontWeight: 700, fontSize: '0.78rem', cursor: 'pointer'
                        }}
                    >
                        Propor Encerramento com Saldo
                    </button>
                </div>
            )}
        </div>
    );
}

function CoverageCell({ label, value, highlight, muted }: { label: string; value: string; highlight?: string; muted?: boolean }) {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
            <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>{label}</span>
            <span style={{ fontWeight: 800, color: muted ? '#92400e' : highlight ?? 'var(--color-text-main)', fontSize: muted ? '0.78rem' : undefined }}>
                {value}
            </span>
        </div>
    );
}

// ── Invoice card ────────────────────────────────────────────────────────────────────────────

function InvoiceCard({
    invoice, canWrite, canDecide, onEdit, onAllocate, onValidate, onLifecycle, onReplace
}: {
    invoice: OperationInvoiceDto;
    canWrite: boolean;
    canDecide: boolean;
    onEdit: () => void;
    onAllocate: () => void;
    onValidate: () => void;
    onLifecycle: (action: LifecycleAction) => void;
    onReplace: () => void;
}) {
    const status = documentStatusPresentation(invoice.status);
    const awaiting = isInvoiceAwaitingDecision(invoice.status);

    const options = [
        ...(canWrite && awaiting ? [
            { label: 'Distribuir Fatura Final', onClick: onAllocate },
            { label: 'Editar', onClick: onEdit }
        ] : []),
        ...(canDecide && invoice.status === 'PENDING_VALIDATION' ? [
            { label: 'Validar Fatura', onClick: onValidate },
            { label: 'Rejeitar Fatura', onClick: () => onLifecycle('reject') }
        ] : []),
        ...(canWrite && awaiting ? [
            { label: 'Anular (registada por engano)', onClick: () => onLifecycle('void') }
        ] : []),
        ...(canDecide && invoice.status === 'VALIDATED' && !invoice.supersededByOperationInvoiceId ? [
            { label: 'Substituir (corrigir fatura validada)', onClick: onReplace },
            { label: 'Ver distribuição', onClick: onAllocate }
        ] : []),
        ...(!awaiting && invoice.status !== 'VALIDATED' ? [
            { label: 'Ver distribuição', onClick: onAllocate }
        ] : []),
        {
            label: 'Descarregar anexo',
            onClick: () => void api.attachments.download(invoice.attachmentId, invoice.attachmentFileName || 'fatura.pdf')
        }
    ];

    return (
        <div style={{
            border: '1px solid var(--color-border)', borderRadius: '10px', padding: '12px 16px',
            display: 'flex', justifyContent: 'space-between', gap: '12px', backgroundColor: '#fff'
        }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                    <span style={{ fontWeight: 800 }}>
                        {invoice.documentNumber || 'Sem número'}
                        {invoice.documentSeries ? ` · Série ${invoice.documentSeries}` : ''}
                    </span>
                    <StatusChip label={status.label} severity={status.severity} />
                    {invoice.amountsEnteredManually && (
                        <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                            Valores informados manualmente
                        </span>
                    )}
                </div>
                <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                    {invoice.supplierName || '—'}
                    {invoice.documentDate ? ` · Doc: ${new Date(invoice.documentDate).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}` : ''}
                    {invoice.dueDate ? ` · Venc: ${new Date(invoice.dueDate).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}` : ''}
                </div>
                <div style={{ fontSize: '0.85rem', display: 'flex', gap: '14px', flexWrap: 'wrap' }}>
                    <span><b>Total:</b> {formatMoney(invoice.grossAmount, invoice.currency)}</span>
                    {invoice.netAmount != null && <span><b>Líquido:</b> {formatMoney(invoice.netAmount, invoice.currency)}</span>}
                    {invoice.taxAmount != null && <span><b>Imposto:</b> {formatMoney(invoice.taxAmount, invoice.currency)}</span>}
                </div>
                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                    Registada por {invoice.uploadedByName || '—'} em {new Date(invoice.uploadedAtUtc).toLocaleDateString('pt-BR')}
                    {invoice.validatedAtUtc && ` · Validada em ${new Date(invoice.validatedAtUtc).toLocaleDateString('pt-BR')}`}
                    {invoice.rejectionReason && invoice.status === 'REJECTED' && ` · Motivo: ${invoice.rejectionReason}`}
                    {invoice.voidReason && ` · Motivo: ${invoice.voidReason}`}
                </div>
            </div>
            <div><KebabMenu options={options} /></div>
        </div>
    );
}
