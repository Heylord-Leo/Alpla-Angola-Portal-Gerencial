import React, { useState } from 'react';
import {
    User, Building2, MapPin, Calendar, DollarSign,
    CheckCircle, XCircle, Clock, ChevronRight, AlertTriangle, Banknote
} from 'lucide-react';
import { ContractApprovalItem, CONTRACT_STATUS_MAP } from '../../../lib/contractsApi';
import { formatDate, formatCurrencyAO } from '../../../lib/utils';

interface ContractApprovalPanelProps {
    contract: ContractApprovalItem;
    stage: 'TECHNICAL' | 'FINAL';
    onApprove: (comment?: string) => Promise<void>;
    onReturn: (comment: string) => Promise<void>;
    onClose?: () => void; // optional — parent drawer handles overlay close
}

export function ContractApprovalPanel({ contract, stage, onApprove, onReturn }: ContractApprovalPanelProps) {
    const [actionLoading, setActionLoading] = useState(false);
    const [showReturnForm, setShowReturnForm] = useState(false);
    const [showApproveConfirm, setShowApproveConfirm] = useState(false);
    const [approveComment, setApproveComment] = useState('');
    const [returnComment, setReturnComment] = useState('');
    const [error, setError] = useState<string | null>(null);

    const statusInfo = CONTRACT_STATUS_MAP[contract.statusCode] ?? { label: contract.statusCode, color: '#6b7280', bg: '#f3f4f6' };

    const counterpartyDisplay = contract.supplierName ?? contract.counterpartyName ?? '—';
    const valueDisplay = contract.totalContractValue != null
        ? formatCurrencyAO(contract.totalContractValue, contract.currencyCode)
        : '—';

    const stageLabel = stage === 'TECHNICAL' ? 'Aprovação Técnica' : 'Aprovação Final';
    const approveLabel = stage === 'TECHNICAL' ? 'Aprovar Tecnicamente' : 'Aprovar e Ativar';

    const handleApprove = async () => {
        setActionLoading(true);
        setError(null);
        try {
            await onApprove(approveComment.trim() || undefined);
            setShowApproveConfirm(false);
        } catch (err: any) {
            setError(err?.message ?? 'Erro ao aprovar contrato.');
        } finally {
            setActionLoading(false);
        }
    };

    const handleReturn = async () => {
        if (!returnComment.trim()) {
            setError('O motivo da devolução é obrigatório.');
            return;
        }
        setActionLoading(true);
        setError(null);
        try {
            await onReturn(returnComment.trim());
            setShowReturnForm(false);
        } catch (err: any) {
            setError(err?.message ?? 'Erro ao devolver contrato.');
        } finally {
            setActionLoading(false);
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', height: '100%', background: 'var(--color-background)', overflow: 'hidden' }}>

            {/* Header */}
            <div style={{
                padding: '20px 24px 16px',
                borderBottom: '1px solid var(--color-border)',
                background: 'var(--color-surface)',
                flexShrink: 0
            }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                        <div style={{
                            width: 36, height: 36, borderRadius: 8,
                            background: stage === 'TECHNICAL' ? '#e0f2fe' : '#ede9fe',
                            display: 'flex', alignItems: 'center', justifyContent: 'center'
                        }}>
                            {stage === 'TECHNICAL'
                                ? <Clock size={18} color="#0369a1" />
                                : <CheckCircle size={18} color="#7c3aed" />
                            }
                        </div>
                        <div>
                            <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                {stageLabel}
                            </div>
                            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--color-text)' }}>
                                {contract.contractNumber}
                            </div>
                        </div>
                    </div>
                    <div style={{
                        display: 'inline-flex', alignItems: 'center', gap: 5,
                        padding: '3px 10px', borderRadius: 6, fontSize: 12, fontWeight: 600,
                        background: statusInfo.bg, color: statusInfo.color
                    }}>
                        {statusInfo.label}
                    </div>
                </div>
                <div style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text)', lineHeight: 1.3 }}>
                    {contract.title}
                </div>
                <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginTop: 2 }}>
                    {contract.contractTypeName}
                </div>
            </div>

            {/* Body */}
            <div style={{ flex: 1, overflowY: 'auto', padding: '20px 24px' }}>

                {/* Key info grid */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 20 }}>
                    <InfoCard icon={<User size={14} />} label="Contraparte" value={counterpartyDisplay} />
                    <InfoCard icon={<Building2 size={14} />} label="Departamento" value={contract.departmentName} />
                    <InfoCard icon={<Building2 size={14} />} label="Empresa" value={contract.companyName} />
                    {contract.plantName && (
                        <InfoCard icon={<MapPin size={14} />} label="Planta" value={contract.plantName} />
                    )}
                    <InfoCard icon={<Calendar size={14} />} label="Vigência" value={`${formatDate(contract.effectiveDateUtc)} → ${formatDate(contract.expirationDateUtc)}`} />
                    <InfoCard icon={<DollarSign size={14} />} label="Valor Total" value={valueDisplay} highlight />
                </div>

                {/* Payment rule */}
                {contract.paymentRuleSummary && (
                    <div style={{
                        background: 'var(--color-surface)', border: '1px solid var(--color-border)',
                        borderRadius: 10, padding: '12px 16px', marginBottom: 16
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 6 }}>
                            <Banknote size={14} color="var(--color-text-muted)" />
                            <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Regra de Pagamento
                            </span>
                        </div>
                        <div style={{ fontSize: 13, color: 'var(--color-text)', lineHeight: 1.5 }}>
                            {contract.paymentRuleSummary}
                        </div>
                    </div>
                )}

                {/* Approver context */}
                <div style={{
                    background: 'var(--color-surface)', border: '1px solid var(--color-border)',
                    borderRadius: 10, padding: '12px 16px', marginBottom: 16
                }}>
                    <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 10 }}>
                        Cadeia de Aprovação
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                        <ApproverStep
                            label="Aprovador Técnico"
                            name={contract.technicalApproverName}
                            active={stage === 'TECHNICAL'}
                            done={stage === 'FINAL'}
                        />
                        <ChevronRight size={14} color="var(--color-text-muted)" />
                        <ApproverStep
                            label="Aprovador Final"
                            name={contract.finalApproverName}
                            active={stage === 'FINAL'}
                            done={false}
                        />
                    </div>
                </div>

                {/* Submitter */}
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                    Submetido por <strong>{contract.createdByUserName}</strong> em {formatDate(contract.createdAtUtc)}
                </div>

                {/* Error */}
                {error && (
                    <div style={{
                        marginTop: 16, padding: '10px 14px', borderRadius: 8,
                        background: '#fef2f2', border: '1px solid #fecaca',
                        display: 'flex', alignItems: 'flex-start', gap: 8,
                        fontSize: 13, color: '#dc2626'
                    }}>
                        <AlertTriangle size={14} style={{ flexShrink: 0, marginTop: 1 }} />
                        {error}
                    </div>
                )}

                {/* Approve confirmation inline */}
                {showApproveConfirm && (
                    <div style={{
                        marginTop: 16, padding: '14px 16px',
                        background: 'var(--color-surface)', border: '1px solid var(--color-border)',
                        borderRadius: 10
                    }}>
                        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--color-text)', marginBottom: 8 }}>
                            Comentário (opcional)
                        </div>
                        <textarea
                            rows={2}
                            value={approveComment}
                            onChange={e => setApproveComment(e.target.value)}
                            placeholder="Ex: Tecnicamente alinhado ao escopo do projeto."
                            style={{
                                width: '100%', boxSizing: 'border-box',
                                padding: '8px 10px', borderRadius: 6,
                                border: '1px solid var(--color-border)',
                                fontSize: 13, resize: 'vertical',
                                background: 'var(--color-background)',
                                color: 'var(--color-text)'
                            }}
                        />
                        <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                            <button
                                onClick={handleApprove}
                                disabled={actionLoading}
                                style={{
                                    flex: 1, padding: '8px 0', borderRadius: 7, border: 'none',
                                    background: stage === 'TECHNICAL' ? '#0369a1' : '#7c3aed',
                                    color: '#fff', fontWeight: 600, fontSize: 13, cursor: 'pointer',
                                    opacity: actionLoading ? 0.6 : 1
                                }}
                            >
                                {actionLoading ? 'Aprovando...' : approveLabel}
                            </button>
                            <button
                                onClick={() => { setShowApproveConfirm(false); setError(null); }}
                                disabled={actionLoading}
                                style={{
                                    padding: '8px 14px', borderRadius: 7,
                                    border: '1px solid var(--color-border)',
                                    background: 'transparent', color: 'var(--color-text)',
                                    fontWeight: 500, fontSize: 13, cursor: 'pointer'
                                }}
                            >
                                Cancelar
                            </button>
                        </div>
                    </div>
                )}

                {/* Return form inline */}
                {showReturnForm && (
                    <div style={{
                        marginTop: 16, padding: '14px 16px',
                        background: '#fef2f2', border: '1px solid #fecaca',
                        borderRadius: 10
                    }}>
                        <div style={{ fontSize: 13, fontWeight: 600, color: '#dc2626', marginBottom: 8 }}>
                            Motivo da Devolução <span style={{ color: '#dc2626' }}>*</span>
                        </div>
                        <textarea
                            rows={3}
                            value={returnComment}
                            onChange={e => { setReturnComment(e.target.value); setError(null); }}
                            placeholder="Descreva o motivo da devolução para que o solicitante possa corrigir..."
                            style={{
                                width: '100%', boxSizing: 'border-box',
                                padding: '8px 10px', borderRadius: 6,
                                border: '1px solid #fecaca',
                                fontSize: 13, resize: 'vertical',
                                background: '#fff', color: '#1f2937'
                            }}
                        />
                        <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                            <button
                                onClick={handleReturn}
                                disabled={actionLoading || !returnComment.trim()}
                                style={{
                                    flex: 1, padding: '8px 0', borderRadius: 7, border: 'none',
                                    background: '#dc2626', color: '#fff',
                                    fontWeight: 600, fontSize: 13, cursor: 'pointer',
                                    opacity: (actionLoading || !returnComment.trim()) ? 0.6 : 1
                                }}
                            >
                                {actionLoading ? 'Devolvendo...' : 'Confirmar Devolução'}
                            </button>
                            <button
                                onClick={() => { setShowReturnForm(false); setReturnComment(''); setError(null); }}
                                disabled={actionLoading}
                                style={{
                                    padding: '8px 14px', borderRadius: 7,
                                    border: '1px solid #fecaca',
                                    background: 'transparent', color: '#dc2626',
                                    fontWeight: 500, fontSize: 13, cursor: 'pointer'
                                }}
                            >
                                Cancelar
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {/* Footer actions */}
            {!showApproveConfirm && !showReturnForm && (
                <div style={{
                    padding: '16px 24px',
                    borderTop: '1px solid var(--color-border)',
                    background: 'var(--color-surface)',
                    flexShrink: 0,
                    display: 'flex', gap: 10
                }}>
                    <button
                        onClick={() => { setShowApproveConfirm(true); setShowReturnForm(false); setError(null); }}
                        style={{
                            flex: 1, padding: '10px 0', borderRadius: 8, border: 'none',
                            background: stage === 'TECHNICAL' ? '#0369a1' : '#7c3aed',
                            color: '#fff', fontWeight: 600, fontSize: 14, cursor: 'pointer',
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6
                        }}
                    >
                        <CheckCircle size={15} />
                        {approveLabel}
                    </button>
                    <button
                        onClick={() => { setShowReturnForm(true); setShowApproveConfirm(false); setError(null); }}
                        style={{
                            flex: 1, padding: '10px 0', borderRadius: 8,
                            border: '1px solid #fecaca',
                            background: '#fef2f2', color: '#dc2626',
                            fontWeight: 600, fontSize: 14, cursor: 'pointer',
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6
                        }}
                    >
                        <XCircle size={15} />
                        Devolver ao Rascunho
                    </button>
                </div>
            )}
        </div>
    );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function InfoCard({ icon, label, value, highlight }: {
    icon: React.ReactNode;
    label: string;
    value: string;
    highlight?: boolean;
}) {
    return (
        <div style={{
            padding: '10px 12px',
            background: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 8
        }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 4 }}>
                <span style={{ color: 'var(--color-text-muted)' }}>{icon}</span>
                <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                    {label}
                </span>
            </div>
            <div style={{
                fontSize: 13, fontWeight: highlight ? 700 : 500,
                color: highlight ? 'var(--color-text)' : 'var(--color-text)'
            }}>
                {value}
            </div>
        </div>
    );
}

function ApproverStep({ label, name, active, done }: {
    label: string;
    name?: string;
    active: boolean;
    done: boolean;
}) {
    const bgColor = done ? '#d1fae5' : active ? '#dbeafe' : 'var(--color-surface)';
    const textColor = done ? '#059669' : active ? '#1d4ed8' : 'var(--color-text-muted)';
    return (
        <div style={{
            flex: 1, padding: '8px 10px', borderRadius: 8,
            background: bgColor, border: `1px solid ${done ? '#a7f3d0' : active ? '#bfdbfe' : 'var(--color-border)'}`,
            textAlign: 'center'
        }}>
            <div style={{ fontSize: 10, fontWeight: 700, color: textColor, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 3 }}>
                {done ? '✓ ' : ''}{label}
            </div>
            <div style={{ fontSize: 12, fontWeight: 600, color: textColor }}>
                {name ?? '—'}
            </div>
        </div>
    );
}
