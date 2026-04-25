import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    User, Building2, Phone, Mail, CreditCard, FileText,
    CheckCircle, XCircle, AlertTriangle, Clock, Package, Banknote
} from 'lucide-react';

interface PendingFicha {
  id: number;
  name: string;
  taxId: string;
  portalCode: string;
  registrationStatus: string;
  submittedByUserName: string;
  submittedAtUtc: string;
  completionPercentage: number;
  missingItems: string[];
  documentSummary: { total: number; uploaded: number; missing: string[] };
  contactName1?: string;
  contactEmail1?: string;
  contactPhone1?: string;
  bankIban?: string;
  paymentTerms?: string;
  paymentMethod?: string;
  address?: string;
  bankAccountNumber?: string;
  bankSwift?: string;
}

export interface SupplierApprovalPanelProps {
    ficha: PendingFicha;
    onApprove: () => Promise<void>;
    onReturn: (comment: string) => Promise<void>;
    onClose?: () => void;
}

const DOC_TYPE_LABELS: Record<string, string> = {
    CONTRIBUINTE: 'Contribuinte',
    CERTIDAO_COMERCIAL: 'Certidão Comercial',
    DIARIO_REPUBLICA: 'Diário da República',
    ALVARA: 'Alvará',
};

export function SupplierApprovalPanel({ ficha, onApprove, onReturn }: SupplierApprovalPanelProps) {
    const navigate = useNavigate();
    const [actionLoading, setActionLoading] = useState(false);
    const [showReturnForm, setShowReturnForm] = useState(false);
    const [showApproveConfirm, setShowApproveConfirm] = useState(false);
    const [returnComment, setReturnComment] = useState('');
    const [error, setError] = useState<string | null>(null);

    const handleApprove = async () => {
        setActionLoading(true);
        setError(null);
        try {
            await onApprove();
            setShowApproveConfirm(false);
        } catch (err: any) {
            setError(err?.message ?? 'Erro ao aprovar ficha.');
        } finally {
            setActionLoading(false);
        }
    };

    const handleReturn = async () => {
        if (!returnComment.trim()) {
            setError('O motivo do reajuste é obrigatório.');
            return;
        }
        setActionLoading(true);
        setError(null);
        try {
            await onReturn(returnComment.trim());
            setShowReturnForm(false);
        } catch (err: any) {
            setError(err?.message ?? 'Erro ao solicitar reajuste.');
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
                            background: '#fef3c7',
                            display: 'flex', alignItems: 'center', justifyContent: 'center'
                        }}>
                            <Clock size={18} color="#d97706" />
                        </div>
                        <div>
                            <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Aprovação Final
                            </div>
                            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--color-text)' }}>
                                {ficha.portalCode}
                            </div>
                        </div>
                    </div>
                    <div style={{
                        display: 'inline-flex', alignItems: 'center', gap: 5,
                        padding: '3px 10px', borderRadius: 6, fontSize: 12, fontWeight: 600,
                        background: '#fef3c7', color: '#92400e'
                    }}>
                        Pendente de Aprovação
                    </div>
                </div>
                <div style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text)', lineHeight: 1.3 }}>
                    {ficha.name}
                </div>
                <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginTop: 2 }}>
                    Ficha de Fornecedor
                </div>
            </div>

            {/* Body */}
            <div style={{ flex: 1, overflowY: 'auto', padding: '20px 24px' }}>

                {/* Key info grid */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 20 }}>
                    <InfoCard icon={<FileText size={14} />} label="NIF" value={ficha.taxId || '—'} />
                    <InfoCard icon={<Package size={14} />} label="Código Portal" value={ficha.portalCode} />
                    <InfoCard icon={<Building2 size={14} />} label="Morada" value={ficha.address || '—'} />
                    <InfoCard icon={<CheckCircle size={14} />} label="Completude" value={`${ficha.completionPercentage}%`} highlight />
                </div>

                {/* Contact info */}
                <SectionBlock title="Contacto Principal">
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                        <InfoCard icon={<User size={14} />} label="Nome" value={ficha.contactName1 || '—'} />
                        <InfoCard icon={<Mail size={14} />} label="E-mail" value={ficha.contactEmail1 || '—'} />
                        <InfoCard icon={<Phone size={14} />} label="Telemóvel" value={ficha.contactPhone1 || '—'} />
                    </div>
                </SectionBlock>

                {/* Banking */}
                <SectionBlock title="Dados Bancários e Comerciais">
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                        <InfoCard icon={<CreditCard size={14} />} label="IBAN" value={ficha.bankIban || '—'} />
                        <InfoCard icon={<CreditCard size={14} />} label="Conta" value={ficha.bankAccountNumber || '—'} />
                        <InfoCard icon={<CreditCard size={14} />} label="SWIFT" value={ficha.bankSwift || '—'} />
                        <InfoCard icon={<Banknote size={14} />} label="Condições Pgto." value={ficha.paymentTerms || '—'} />
                        <InfoCard icon={<Banknote size={14} />} label="Método Pgto." value={ficha.paymentMethod || '—'} />
                    </div>
                </SectionBlock>

                {/* Documents */}
                <SectionBlock title={`Documentos (${ficha.documentSummary.uploaded}/${ficha.documentSummary.total})`}>
                    {ficha.documentSummary.missing.length === 0 ? (
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, color: '#059669', fontWeight: 600 }}>
                            <CheckCircle size={14} /> Todos os documentos obrigatórios foram carregados.
                        </div>
                    ) : (
                        <div style={{ fontSize: 13, color: '#dc2626' }}>
                            <div style={{ fontWeight: 600, marginBottom: 6 }}>Documentos em falta:</div>
                            <ul style={{ margin: 0, paddingLeft: 18 }}>
                                {ficha.documentSummary.missing.map(d => (
                                    <li key={d} style={{ marginBottom: 3 }}>{DOC_TYPE_LABELS[d] || d}</li>
                                ))}
                            </ul>
                        </div>
                    )}
                </SectionBlock>

                {/* Missing completeness items */}
                {ficha.missingItems.length > 0 && (
                    <SectionBlock title="Itens Incompletos">
                        <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13, color: 'var(--color-text-muted)' }}>
                            {ficha.missingItems.map((item, i) => (
                                <li key={i} style={{ marginBottom: 3 }}>{item}</li>
                            ))}
                        </ul>
                    </SectionBlock>
                )}

                {/* Submitter */}
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 8 }}>
                    Submetido por <strong>{ficha.submittedByUserName}</strong> em {new Date(ficha.submittedAtUtc).toLocaleString('pt-PT')}
                </div>

                {/* Link to full ficha */}
                <div style={{ marginTop: 12 }}>
                    <button
                        onClick={() => navigate(`/contracts/fichas/${ficha.id}`)}
                        style={{
                            background: 'none', border: 'none', color: 'var(--color-primary)',
                            cursor: 'pointer', fontSize: 13, fontWeight: 600, padding: 0,
                            textDecoration: 'underline'
                        }}
                    >
                        Abrir ficha completa →
                    </button>
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
                            Confirma a aprovação e ativação deste fornecedor?
                        </div>
                        <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                            <button
                                onClick={handleApprove}
                                disabled={actionLoading}
                                style={{
                                    flex: 1, padding: '8px 0', borderRadius: 7, border: 'none',
                                    background: '#059669',
                                    color: '#fff', fontWeight: 600, fontSize: 13, cursor: 'pointer',
                                    opacity: actionLoading ? 0.6 : 1
                                }}
                            >
                                {actionLoading ? 'Aprovando...' : 'Confirmar Aprovação'}
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
                            Motivo do Reajuste <span style={{ color: '#dc2626' }}>*</span>
                        </div>
                        <textarea
                            rows={3}
                            value={returnComment}
                            onChange={e => { setReturnComment(e.target.value); setError(null); }}
                            placeholder="Descreva o motivo para que o solicitante possa corrigir..."
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
                                {actionLoading ? 'Enviando...' : 'Confirmar Reajuste'}
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

            {/* Footer actions (sticky) */}
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
                            background: '#059669',
                            color: '#fff', fontWeight: 600, fontSize: 14, cursor: 'pointer',
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6
                        }}
                    >
                        <CheckCircle size={15} />
                        Aprovar e Ativar
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
                        Solicitar Reajuste
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
                color: 'var(--color-text)'
            }}>
                {value}
            </div>
        </div>
    );
}

function SectionBlock({ title, children }: { title: string; children: React.ReactNode }) {
    return (
        <div style={{
            background: 'var(--color-surface)', border: '1px solid var(--color-border)',
            borderRadius: 10, padding: '12px 16px', marginBottom: 16
        }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 10 }}>
                {title}
            </div>
            {children}
        </div>
    );
}

export type { PendingFicha };
export default SupplierApprovalPanel;
