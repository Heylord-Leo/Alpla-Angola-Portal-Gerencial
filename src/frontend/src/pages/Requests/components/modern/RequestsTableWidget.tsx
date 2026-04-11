import React, { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    Search, CheckCircle2, AlertCircle, Clock, CreditCard,
    FileText, XCircle, Briefcase, Package, ArrowUp, ArrowDown,
    ArrowUpDown, CalendarClock, Copy, Eye, ChevronLeft, ChevronRight, User
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { RequestListItemDto } from '../../../../types';
import { KebabMenu } from '../../../../components/ui/KebabMenu';
import { ModernRequestTimeline } from './ModernRequestTimeline';
import { ModernTooltip } from '../../../../components/ui/ModernTooltip';
import { getRequestGuidance, getUrgencyStyle } from '../../../../lib/utils';

// ── Status Badge ──────────────────────────────────────────
const STATUS_THEME: Record<string, { bg: string; fg: string; border: string; icon: any }> = {
    'DRAFT':              { bg: '#F1F5F9', fg: '#475569', border: '#CBD5E1', icon: FileText },
    'AREA_ADJUSTMENT':    { bg: '#FFF7ED', fg: '#C2410C', border: '#FDBA74', icon: AlertCircle },
    'FINAL_ADJUSTMENT':   { bg: '#FFF7ED', fg: '#C2410C', border: '#FDBA74', icon: AlertCircle },
    'WAITING_QUOTATION':  { bg: '#EFF6FF', fg: '#1D4ED8', border: '#93C5FD', icon: Clock },
    'WAITING_AREA_APPROVAL':  { bg: '#FFFBEB', fg: '#B45309', border: '#FCD34D', icon: Clock },
    'WAITING_FINAL_APPROVAL': { bg: '#FFFBEB', fg: '#B45309', border: '#FCD34D', icon: Clock },
    'APPROVED':           { bg: '#ECFDF5', fg: '#047857', border: '#6EE7B7', icon: CheckCircle2 },
    'PO_ISSUED':          { bg: '#EEF2FF', fg: '#4338CA', border: '#A5B4FC', icon: FileText },
    'PAYMENT_SCHEDULED':  { bg: '#FAF5FF', fg: '#7E22CE', border: '#C4B5FD', icon: CreditCard },
    'PAYMENT_COMPLETED':  { bg: '#ECFDF5', fg: '#047857', border: '#6EE7B7', icon: CheckCircle2 },
    'WAITING_RECEIPT':    { bg: '#ECFEFF', fg: '#0E7490', border: '#67E8F9', icon: FileText },
    'COMPLETED':          { bg: '#ECFDF5', fg: '#047857', border: '#6EE7B7', icon: CheckCircle2 },
    'QUOTATION_COMPLETED':{ bg: '#ECFDF5', fg: '#047857', border: '#6EE7B7', icon: CheckCircle2 },
    'CANCELLED':          { bg: '#F1F5F9', fg: '#475569', border: '#CBD5E1', icon: XCircle },
    'REJECTED':           { bg: '#FEF2F2', fg: '#DC2626', border: '#FCA5A5', icon: AlertCircle },
};

export const StatusBadge = ({ status }: { status: string }) => {
    const theme = STATUS_THEME[status] || STATUS_THEME['DRAFT'];
    const Icon = theme.icon;
    const statusText = status.replace(/_/g, ' ');

    return (
        <span style={{
            padding: '3px 10px',
            borderRadius: 'var(--radius-full)',
            fontSize: '0.6rem',
            fontWeight: 800,
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
            display: 'inline-flex',
            alignItems: 'center',
            gap: '4px',
            border: `1px solid ${theme.border}`,
            backgroundColor: theme.bg,
            color: theme.fg,
            whiteSpace: 'nowrap',
        }}>
            <Icon size={11} />
            {statusText}
        </span>
    );
};

// ── Table Widget ──────────────────────────────────────────
export interface RequestsTableWidgetProps {
    requests: RequestListItemDto[];
    loading: boolean;
    sortConfig: { key: string | null; direction: 'asc' | 'desc' };
    onSort: (key: string) => void;
    onRowClick: (requestId: string) => void;
    page: number;
    pageSize: number;
    totalCount: number;
    onPageChange: (page: number) => void;
    onPageSizeChange: (size: number) => void;
}

const COLUMNS = [
    { label: 'Número', key: 'requestNumber' },
    { label: 'Título do Pedido', key: 'title' },
    { label: 'Tipo', key: 'requestTypeCode' },
    { label: 'Empresa / Planta', key: 'companyName' },
    { label: 'Data Limite', key: 'needByDateUtc' },
    { label: 'Status', key: 'statusCode' },
    { label: 'Valor Estimado', key: 'estimatedTotalAmount', align: 'right' as const },
];

export function RequestsTableWidget({
    requests, loading, sortConfig, onSort, onRowClick,
    page, pageSize, totalCount, onPageChange, onPageSizeChange
}: RequestsTableWidgetProps) {

    const navigate = useNavigate();
    const [expandedRequestId, setExpandedRequestId] = useState<string | null>(null);
    const totalPages = Math.ceil(totalCount / pageSize);

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            boxShadow: 'var(--shadow-sm)',
            borderRadius: 'var(--radius-lg)',
            overflow: 'hidden',
            position: 'relative',
        }}>
            {/* Loading overlay */}
            {loading && (
                <div style={{
                    position: 'absolute',
                    inset: 0,
                    backgroundColor: 'rgba(255,255,255,0.6)',
                    backdropFilter: 'blur(2px)',
                    zIndex: 10,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                }}>
                    <div style={{
                        width: 32,
                        height: 32,
                        border: '3px solid var(--color-border)',
                        borderTopColor: 'var(--color-primary)',
                        borderRadius: '50%',
                        animation: 'spin 0.8s linear infinite',
                    }} />
                </div>
            )}

            <div style={{ overflowX: 'auto', minHeight: '300px' }}>
                <table style={{ width: '100%', textAlign: 'left', borderCollapse: 'collapse', margin: 0, border: 'none', boxShadow: 'none', borderRadius: 0 }}>
                    <thead>
                        <tr style={{ backgroundColor: '#FAFAFA', borderBottom: '1px solid var(--color-border)' }}>
                            {COLUMNS.map((col) => (
                                <th
                                    key={col.key}
                                    onClick={() => onSort(col.key)}
                                    style={{
                                        padding: '14px 20px',
                                        fontSize: '0.65rem',
                                        fontWeight: 800,
                                        color: 'var(--color-text-muted)',
                                        textTransform: 'uppercase',
                                        letterSpacing: '0.08em',
                                        cursor: 'pointer',
                                        transition: 'background-color 0.15s ease',
                                        textAlign: col.align === 'right' ? 'right' : 'left',
                                        whiteSpace: 'nowrap',
                                        background: 'transparent',
                                        borderBottom: 'none',
                                    }}
                                >
                                    <div style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: '6px',
                                        justifyContent: col.align === 'right' ? 'flex-end' : 'flex-start',
                                    }}>
                                        {col.label}
                                        {sortConfig.key === col.key ? (
                                            sortConfig.direction === 'asc' ? <ArrowUp size={12} /> : <ArrowDown size={12} />
                                        ) : (
                                            <ArrowUpDown size={12} style={{ opacity: 0.3 }} />
                                        )}
                                    </div>
                                </th>
                            ))}
                            <th style={{
                                padding: '14px 20px',
                                fontSize: '0.65rem',
                                fontWeight: 800,
                                color: 'var(--color-text-muted)',
                                textTransform: 'uppercase',
                                letterSpacing: '0.08em',
                                textAlign: 'center',
                                background: 'transparent',
                                borderBottom: 'none',
                            }}>Ações</th>
                        </tr>
                    </thead>
                    <tbody>
                        {requests.length === 0 && !loading ? (
                            <tr>
                                <td colSpan={8} style={{ padding: '60px 20px', textAlign: 'center', border: 'none' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
                                        <div style={{
                                            width: 64, height: 64,
                                            backgroundColor: 'var(--color-bg-page)',
                                            borderRadius: '50%',
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            color: 'var(--color-text-muted)',
                                            opacity: 0.4,
                                            marginBottom: '16px',
                                        }}>
                                            <Search size={32} />
                                        </div>
                                        <h3 style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '4px' }}>
                                            Nenhum pedido encontrado
                                        </h3>
                                        <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', maxWidth: '320px' }}>
                                            Ajuste os filtros ou o termo de busca para localizar o pedido corporativo.
                                        </p>
                                    </div>
                                </td>
                            </tr>
                        ) : (
                            <AnimatePresence mode="popLayout">
                                {requests.map((req) => {
                                    const deadline = req.needByDateUtc ? new Date(req.needByDateUtc) : null;
                                    const isOverdue = deadline ? deadline.getTime() < new Date().getTime() : false;
                                    const isApproaching = deadline && !isOverdue && (deadline.getTime() - new Date().getTime()) / (1000 * 3600 * 24) <= 7;
                                    const isPayment = req.requestTypeCode === 'PAYMENT';
                                    
                                    const guidance = getRequestGuidance(req.statusCode, req.requestTypeCode);
                                    const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);

                                    return (
                                        <React.Fragment key={req.id.toString()}>
                                            <motion.tr
                                                layout
                                                initial={{ opacity: 0 }}
                                                animate={{ opacity: 1 }}
                                                exit={{ opacity: 0 }}
                                                onClick={() => setExpandedRequestId(expandedRequestId === req.id.toString() ? null : req.id.toString())}
                                                style={{ cursor: 'pointer', borderBottom: '1px solid var(--color-border)', backgroundColor: expandedRequestId === req.id.toString() ? '#F8FAFC' : 'transparent' }}
                                                className="hoverable-row"
                                            >
                                                {/* Número */}
                                                <td style={{ padding: '12px 20px', border: 'none' }}>
                                                    <ModernTooltip content={
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                            <User size={14} color="var(--color-primary)" />
                                                            <span style={{ fontWeight: 700, fontSize: '0.85rem' }}>{req.requesterName}</span>
                                                        </div>
                                                    } side="top">
                                                        <span 
                                                            onClick={(e) => { e.stopPropagation(); onRowClick(req.id.toString()); }}
                                                            style={{
                                                                fontSize: '0.8rem',
                                                                fontFamily: 'monospace',
                                                                color: 'var(--color-primary)',
                                                                fontWeight: 700,
                                                                cursor: 'pointer',
                                                            }}
                                                            onMouseEnter={(e) => e.currentTarget.style.textDecoration = 'underline'}
                                                            onMouseLeave={(e) => e.currentTarget.style.textDecoration = 'none'}
                                                        >{req.requestNumber || 'S/N'}</span>
                                                    </ModernTooltip>
                                                </td>
                                            {/* Título */}
                                            <td style={{ padding: '12px 20px', border: 'none' }}>
                                                <p style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0, maxWidth: '200px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                                    {req.title || 'Sem título'}
                                                </p>
                                                <p style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', margin: '2px 0 0', fontWeight: 500, textTransform: 'none' }}>
                                                    {req.departmentName || 'Desconhecido'}
                                                </p>
                                            </td>
                                            {/* Tipo */}
                                            <td style={{ padding: '12px 20px', border: 'none' }}>
                                                <ModernTooltip content={
                                                    <span style={{ fontWeight: 700, textTransform: 'uppercase', fontSize: '0.75rem' }}>
                                                        {req.requestTypeName}
                                                    </span>
                                                } side="top">
                                                    <div style={{
                                                        width: 32, height: 32,
                                                        borderRadius: 'var(--radius-md)',
                                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                                        backgroundColor: isPayment ? '#ECFDF5' : '#EFF6FF',
                                                        color: isPayment ? 'var(--color-status-emerald)' : 'var(--color-primary)',
                                                    }}>
                                                        {isPayment ? <Briefcase size={14} /> : <Package size={14} />}
                                                    </div>
                                                </ModernTooltip>
                                            </td>
                                            {/* Empresa / Planta */}
                                            <td style={{ padding: '12px 20px', border: 'none' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-main)', maxWidth: '150px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                                        {req.companyName || 'Alpla'}
                                                    </span>
                                                    <span style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                                                        {req.plantName || 'Genérico'}
                                                    </span>
                                                </div>
                                            </td>
                                            {/* Data Limite */}
                                            <td style={{ padding: '12px 20px', border: 'none' }}>
                                                {deadline ? (
                                                    <ModernTooltip content={urgency ? urgency.description : 'Data limite'} side="top">
                                                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: '4px' }}>
                                                            <div style={{
                                                                display: 'flex', alignItems: 'center', gap: '6px',
                                                                fontSize: '0.8rem',
                                                                color: isOverdue ? 'var(--color-status-red)' : 'var(--color-text-main)',
                                                                fontWeight: isOverdue ? 700 : 500,
                                                            }}>
                                                                <CalendarClock size={12} style={{ color: isOverdue ? 'var(--color-status-red)' : 'var(--color-text-muted)' }} />
                                                                {deadline.toLocaleDateString('pt-BR')}
                                                            </div>
                                                            {isOverdue && <OverdueTag text="Atrasado" bg="#FEF2F2" fg="var(--color-status-red)" />}
                                                            {isApproaching && <OverdueTag text="Próximo" bg="#FFFBEB" fg="var(--color-status-amber)" />}
                                                        </div>
                                                    </ModernTooltip>
                                                ) : <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>N/A</span>}
                                            </td>
                                            {/* Status */}
                                            <td style={{ padding: '12px 20px', border: 'none' }}>
                                                <ModernTooltip content={
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                        <div><div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Situação Atual</div><div style={{ fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)' }}>{guidance.responsible}</div></div>
                                                        <div style={{ borderTop: '1px solid var(--color-border)', paddingTop: '8px' }}><div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)' }}>Próxima Ação</div><div style={{ fontSize: '0.8rem', fontWeight: 600, fontStyle: 'italic', color: 'var(--color-text-main)' }}>{guidance.nextAction}</div></div>
                                                    </div>
                                                } side="top" align="start">
                                                    <StatusBadge status={req.statusCode || 'DRAFT'} />
                                                </ModernTooltip>
                                            </td>
                                            {/* Valor */}
                                            <td style={{ padding: '12px 20px', textAlign: 'right', border: 'none' }}>
                                                <p style={{
                                                    fontSize: '0.85rem',
                                                    fontWeight: 800,
                                                    color: 'var(--color-text-main)',
                                                    margin: 0,
                                                    whiteSpace: 'nowrap',
                                                }}>
                                                    {req.currencyCode || 'AOA'} {(req.estimatedTotalAmount || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2 })}
                                                </p>
                                            </td>
                                            {/* Ações */}
                                            <td style={{ padding: '12px 20px', textAlign: 'center', border: 'none' }} onClick={(e) => e.stopPropagation()}>
                                                <KebabMenu options={[
                                                    { label: 'Vis. Rápida', icon: <Eye size={16} />, onClick: () => onRowClick(req.id.toString()) },
                                                    { label: 'Duplicar', icon: <Copy size={16} />, onClick: () => navigate(`/requests/new?copyFrom=${req.id}`) }
                                                ]} />
                                            </td>
                                        </motion.tr>
                                            
                                            {/* Timeline Expanded Row */}
                                            <AnimatePresence>
                                                {expandedRequestId === req.id.toString() && (
                                                    <motion.tr 
                                                        initial={{ opacity: 0, height: 0 }}
                                                        animate={{ opacity: 1, height: 'auto' }}
                                                        exit={{ opacity: 0, height: 0 }}
                                                        style={{ 
                                                            borderBottom: '1px solid var(--color-border)',
                                                            backgroundColor: 'var(--color-bg-subtle)' 
                                                        }}
                                                    >
                                                        <td colSpan={8} style={{ padding: 0, border: 'none' }}>
                                                            <div style={{ boxShadow: 'inset 0 4px 6px -4px rgba(0,0,0,0.05)', backgroundColor: '#F8FAFC' }}>
                                                                <ModernRequestTimeline requestId={req.id.toString()} />
                                                            </div>
                                                        </td>
                                                    </motion.tr>
                                                )}
                                            </AnimatePresence>
                                        </React.Fragment>
                                    );
                                })}
                            </AnimatePresence>
                        )}
                    </tbody>
                </table>
            </div>

            {/* ── Pagination Footer ── */}
            <div style={{
                padding: '12px 20px',
                backgroundColor: '#FAFAFA',
                borderTop: '1px solid var(--color-border)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: '0.75rem',
                color: 'var(--color-text-muted)',
                flexWrap: 'wrap',
                gap: '32px',
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span>Pág:</span>
                    <select
                        value={pageSize.toString()}
                        onChange={(e) => { onPageSizeChange(Number(e.target.value)); onPageChange(1); }}
                        style={{
                            padding: '2px 6px',
                            fontSize: '0.75rem',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'var(--color-bg-surface)',
                            color: 'var(--color-text-main)',
                        }}
                    >
                        <option value="20">20</option>
                        <option value="50">50</option>
                        <option value="100">100</option>
                    </select>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <span>
                        Mostrando {requests.length > 0 ? (page - 1) * pageSize + 1 : 0} - {Math.min(page * pageSize, totalCount)} de {totalCount}
                    </span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <PaginationButton onClick={() => onPageChange(page - 1)} disabled={page === 1}>
                            <ChevronLeft size={14} />
                        </PaginationButton>
                        <span style={{ fontWeight: 700 }}>{page} / {totalPages > 0 ? totalPages : 1}</span>
                        <PaginationButton onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
                            <ChevronRight size={14} />
                        </PaginationButton>
                    </div>
                </div>
            </div>
        </div>
    );
}

// ── Helpers ──

function OverdueTag({ text, bg, fg }: { text: string; bg: string; fg: string }) {
    return (
        <span style={{
            fontSize: '0.55rem',
            fontWeight: 800,
            textTransform: 'uppercase',
            letterSpacing: '0.06em',
            color: fg,
            backgroundColor: bg,
            padding: '2px 6px',
            borderRadius: 'var(--radius-sm)',
        }}>{text}</span>
    );
}

function PaginationButton({ onClick, disabled, children }: { onClick: () => void; disabled: boolean; children: React.ReactNode }) {
    return (
        <button
            disabled={disabled}
            onClick={onClick}
            style={{
                padding: '6px',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-sm)',
                backgroundColor: 'var(--color-bg-surface)',
                cursor: disabled ? 'default' : 'pointer',
                opacity: disabled ? 0.4 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                transition: 'background-color 0.15s ease',
                color: 'var(--color-text-main)',
            }}
        >
            {children}
        </button>
    );
}
