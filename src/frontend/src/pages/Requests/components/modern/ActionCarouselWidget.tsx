import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { ChevronLeft, ChevronRight, FileText, Clock, AlertCircle, CreditCard, Briefcase, Package, Building2, MapPin, Eye, Copy } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { DashboardSummaryDto, RequestListItemDto } from '../../../../types';
import { api } from '../../../../lib/api';
import { StatusBadge } from './RequestsTableWidget';
import { KebabMenu } from '../../../../components/ui/KebabMenu';

export interface ActionCarouselWidgetProps {
    summary: DashboardSummaryDto;
    onRowClick?: (id: string) => void;
    onCorrectPoClick?: (requestId: string) => void;
}

// ── Stat card icon background/foreground color pairs ──
const STAT_THEMES: Record<string, { bg: string; fg: string }> = {
    blue:    { bg: '#EFF6FF', fg: 'var(--color-primary)' },
    amber:   { bg: '#FFFBEB', fg: 'var(--color-status-amber)' },
    rose:    { bg: '#FFF1F2', fg: 'var(--color-status-rose)' },
    slate:   { bg: '#F1F5F9', fg: 'var(--color-status-slate)' },
    emerald: { bg: '#ECFDF5', fg: 'var(--color-status-emerald)' },
};

export function ActionCarouselWidget({ summary, onRowClick, onCorrectPoClick }: ActionCarouselWidgetProps) {
    const navigate = useNavigate();
    const [actionRequests, setActionRequests] = useState<RequestListItemDto[]>([]);
    const [carouselIndex, setCarouselIndex] = useState(0);

    const itemsPerPage = 3;
    const maxCarouselIndex = Math.max(0, actionRequests.length - itemsPerPage);

    const nextCarousel = () => setCarouselIndex(prev => Math.min(prev + 1, maxCarouselIndex));
    const prevCarousel = () => setCarouselIndex(prev => Math.max(prev - 1, 0));

    useEffect(() => {
        const fetchAttentionRequests = async () => {
            try {
                const data = await api.requests.list(undefined, { isAttention: true }, 1, 15);
                const pagedResult = data.pagedResult || (data as any).PagedResult;
                const items = pagedResult ? pagedResult.items || (pagedResult as any).Items : ((data as any).items || []);
                setActionRequests(items);
            } catch (err) {
                console.error(err);
            }
        };
        fetchAttentionRequests();
    }, [summary]); // Dependency on summary so it re-fetches when dashboard reloads data

    const stats = [
        { label: 'Total de Pedidos', value: summary.totalRequests || 0, icon: FileText, theme: 'blue' },
        { label: 'Em Cotação', value: summary.waitingQuotation || 0, icon: Clock, theme: 'amber' },
        { label: 'Pend. Aprovação', value: (summary as any).pendingMyApproval || 0, icon: AlertCircle, theme: 'rose' },
        { label: 'Rascunhos', value: (summary as any).draftRequests || 0, icon: FileText, theme: 'slate' },
        { label: 'Pend. Finanças', value: (summary as any).pendingFinanceAction || 0, icon: CreditCard, theme: 'emerald' },
    ];

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>

            {/* ── Stats Grid ── */}
            <section style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(5, 1fr)',
                gap: '16px',
            }}>
                {stats.map((stat, i) => {
                    const colors = STAT_THEMES[stat.theme] || STAT_THEMES.blue;
                    return (
                        <motion.div
                            key={stat.label}
                            initial={{ opacity: 0, y: 20 }}
                            animate={{ opacity: 1, y: 0 }}
                            transition={{ delay: i * 0.1 }}
                            style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-border)',
                                padding: '20px',
                                borderRadius: 'var(--radius-lg)',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: '12px',
                                position: 'relative',
                                overflow: 'hidden',
                                boxShadow: 'var(--shadow-sm)',
                                transition: 'box-shadow 0.2s ease',
                                cursor: 'default',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.boxShadow = 'var(--shadow-md)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.boxShadow = 'var(--shadow-sm)'; }}
                        >
                            {/* Icon */}
                            <div style={{
                                width: 40,
                                height: 40,
                                backgroundColor: colors.bg,
                                color: colors.fg,
                                borderRadius: 'var(--radius-md)',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                            }}>
                                <stat.icon size={20} />
                            </div>
                            {/* Text */}
                            <div style={{ position: 'relative', zIndex: 1 }}>
                                <p style={{
                                    fontSize: '0.7rem',
                                    fontWeight: 700,
                                    color: 'var(--color-text-muted)',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.06em',
                                    margin: 0,
                                }}>{stat.label}</p>
                                <p style={{
                                    fontSize: '1.75rem',
                                    fontWeight: 900,
                                    color: 'var(--color-text-main)',
                                    margin: '4px 0 0',
                                    lineHeight: 1,
                                    fontFamily: 'var(--font-family-display)',
                                }}>{stat.value}</p>
                            </div>
                            {/* Background watermark icon */}
                            <div style={{
                                position: 'absolute',
                                right: -8,
                                bottom: -8,
                                opacity: 0.04,
                                pointerEvents: 'none',
                            }}>
                                <stat.icon size={80} />
                            </div>
                        </motion.div>
                    );
                })}
            </section>

            {/* ── Action Required Carousel ── */}
            {actionRequests.length > 0 && (
                <section style={{
                    overflow: 'hidden',
                    paddingTop: '16px',
                    borderTop: '1px solid var(--color-border)',
                }}>
                    {/* Header */}
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                        <div>
                            <h2 style={{
                                fontSize: '1.1rem',
                                fontWeight: 800,
                                color: 'var(--color-primary)',
                                margin: 0,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                            }}>
                                Para Minha Ação
                                <span style={{
                                    backgroundColor: '#FFF1F2',
                                    color: 'var(--color-status-rose)',
                                    fontSize: '0.65rem',
                                    fontWeight: 800,
                                    padding: '2px 8px',
                                    borderRadius: 'var(--radius-full)',
                                }}>{actionRequests.length}</span>
                            </h2>
                            <p style={{
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)',
                                margin: '4px 0 0',
                                fontWeight: 500,
                            }}>Pedidos atribuídos a você ou que requerem atenção imediata.</p>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <NavButton onClick={prevCarousel} disabled={carouselIndex === 0}>
                                <ChevronLeft size={16} />
                            </NavButton>
                            <NavButton onClick={nextCarousel} disabled={carouselIndex >= maxCarouselIndex}>
                                <ChevronRight size={16} />
                            </NavButton>
                        </div>
                    </div>

                    {/* Carousel Track */}
                    <div style={{ position: 'relative', overflow: 'visible' }}>
                        <motion.div
                            style={{ display: 'flex', gap: '16px' }}
                            animate={{ x: `calc(-${carouselIndex * (100 / itemsPerPage)}% - ${carouselIndex * (16 / itemsPerPage)}px)` }}
                            transition={{ type: "spring", stiffness: 300, damping: 30 }}
                        >
                            {actionRequests.map((order) => (
                                <div
                                    key={order.id}
                                    style={{
                                        minWidth: 'calc(33.333% - 11px)',
                                        flexShrink: 0,
                                        cursor: 'pointer',
                                    }}
                                    onClick={() => onRowClick && onRowClick(order.id.toString())}
                                >
                                    <CarouselCard 
                                        order={order} 
                                        onView={() => onRowClick && onRowClick(order.id.toString())}
                                        onOpenFull={() => navigate(`/requests/${order.id}`)}
                                        onDuplicate={() => navigate(`/requests/new?copyFrom=${order.id}`)}
                                        onQuotationClick={() => navigate(`/buyer/items?highlightRequestId=${order.id}`)}
                                        onReceivingClick={() => navigate(`/receiving/operation/${order.id}?highlightRequestId=${order.id}`)}
                                        onPaymentClick={() => navigate(`/finance/payments?highlightRequestId=${order.id}`)}
                                        onCorrectPoClick={onCorrectPoClick ? () => onCorrectPoClick(order.id.toString()) : undefined}
                                    />
                                </div>
                            ))}
                        </motion.div>
                    </div>
                </section>
            )}
        </div>
    );
}

// ── Sub-components ──

function NavButton({ onClick, disabled, children }: { onClick: () => void; disabled: boolean; children: React.ReactNode }) {
    return (
        <button
            onClick={onClick}
            disabled={disabled}
            style={{
                padding: '6px',
                border: '1px solid var(--color-border)',
                backgroundColor: 'var(--color-bg-surface)',
                borderRadius: 'var(--radius-md)',
                cursor: disabled ? 'default' : 'pointer',
                opacity: disabled ? 0.3 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                transition: 'all 0.15s ease',
                boxShadow: 'var(--shadow-sm)',
                color: 'var(--color-text-main)',
            }}
        >
            {children}
        </button>
    );
}

function CarouselCard({ order, onView, onOpenFull, onDuplicate, onQuotationClick, onReceivingClick, onPaymentClick, onCorrectPoClick }: { order: RequestListItemDto; onView: () => void; onOpenFull: () => void; onDuplicate: () => void; onQuotationClick?: () => void; onReceivingClick?: () => void; onPaymentClick?: () => void; onCorrectPoClick?: () => void; }) {
    const isPayment = order.requestTypeCode === 'PAYMENT';

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            padding: '20px',
            borderRadius: 'var(--radius-lg)',
            boxShadow: 'var(--shadow-sm)',
            transition: 'box-shadow 0.2s ease, border-color 0.2s ease',
            height: '100%',
            display: 'flex',
            flexDirection: 'column',
        }}
            onMouseEnter={(e) => {
                e.currentTarget.style.boxShadow = 'var(--shadow-lg)';
                e.currentTarget.style.borderColor = 'var(--color-primary)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
                e.currentTarget.style.borderColor = 'var(--color-border)';
            }}
        >
            {/* Top row */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <div style={{
                        padding: '8px',
                        borderRadius: 'var(--radius-md)',
                        backgroundColor: isPayment ? '#ECFDF5' : '#EFF6FF',
                        color: isPayment ? 'var(--color-status-emerald)' : 'var(--color-primary)',
                    }}>
                        {isPayment ? <Briefcase size={16} /> : <Package size={16} />}
                    </div>
                    <span style={{
                        fontSize: '0.75rem',
                        fontFamily: 'monospace',
                        color: 'var(--color-text-muted)',
                        fontWeight: 600,
                    }}>{order.requestNumber || 'S/N'}</span>
                </div>
                <div onClick={(e) => e.stopPropagation()}>
                    <KebabMenu options={[
                        { label: 'Vis. Rápida', icon: <Eye size={16} />, onClick: onView },
                        { label: 'Duplicar', icon: <Copy size={16} />, onClick: onDuplicate }
                    ]} />
                </div>
            </div>

            {/* Title */}
            <h3 style={{
                fontWeight: 700,
                color: 'var(--color-text-main)',
                marginBottom: '8px',
                fontSize: '0.95rem',
                lineHeight: 1.4,
                minHeight: '40px',
                overflow: 'hidden',
                display: '-webkit-box',
                WebkitLineClamp: 2,
                WebkitBoxOrient: 'vertical',
                textTransform: 'none',
            }}>{order.title || 'Sem título'}</h3>

            {/* Meta info */}
            <div style={{
                display: 'flex',
                flexWrap: 'wrap',
                gap: '8px',
                fontSize: '0.65rem',
                color: 'var(--color-text-muted)',
                marginBottom: '16px',
                marginTop: 'auto',
            }}>
                <MetaTag icon={<Building2 size={10} />} text={order.companyName || 'Alpla'} />
                <MetaTag icon={<MapPin size={10} />} text={order.plantName || ''} />
            </div>

            {/* Footer */}
            <div style={{
                display: 'flex',
                alignItems: 'flex-end',
                justifyContent: 'space-between',
                paddingTop: '16px',
                borderTop: '1px solid var(--color-border)',
                marginTop: '8px',
            }}>
                <div style={{ flex: 1 }}>
                    {order.statusCode === 'WAITING_QUOTATION' && onQuotationClick ? (
                        <div 
                            onClick={(e) => {
                                e.stopPropagation();
                                onQuotationClick();
                            }}
                            title="Ir para a tela de cotações"
                            style={{ 
                                cursor: 'pointer', 
                                display: 'inline-block',
                                transition: 'all 0.2s',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.opacity = '0.7'; e.currentTarget.style.transform = 'scale(1.02)'; }}
                            onMouseLeave={e => { e.currentTarget.style.opacity = '1'; e.currentTarget.style.transform = 'scale(1)'; }}
                        >
                            <StatusBadge status={order.statusCode} />
                        </div>
                    ) : (order.statusCode === 'WAITING_RECEIPT' || order.statusCode === 'PAYMENT_COMPLETED') && onReceivingClick ? (
                        <div 
                            onClick={(e) => {
                                e.stopPropagation();
                                onReceivingClick();
                            }}
                            title="Ir para a tela de recebimento"
                            style={{ 
                                cursor: 'pointer', 
                                display: 'inline-block',
                                transition: 'all 0.2s',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.opacity = '0.7'; e.currentTarget.style.transform = 'scale(1.02)'; }}
                            onMouseLeave={e => { e.currentTarget.style.opacity = '1'; e.currentTarget.style.transform = 'scale(1)'; }}
                        >
                            <StatusBadge status={order.statusCode} />
                        </div>
                    ) : (order.statusCode === 'PO_ISSUED' || order.statusCode === 'PAYMENT_SCHEDULED' || order.statusCode === 'PAYMENT_REQUEST_SENT') && onPaymentClick ? (
                        <div 
                            onClick={(e) => {
                                e.stopPropagation();
                                onPaymentClick();
                            }}
                            title="Ir para tela de finanças"
                            style={{ 
                                cursor: 'pointer', 
                                display: 'inline-block',
                                transition: 'all 0.2s',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.opacity = '0.7'; e.currentTarget.style.transform = 'scale(1.02)'; }}
                            onMouseLeave={e => { e.currentTarget.style.opacity = '1'; e.currentTarget.style.transform = 'scale(1)'; }}
                        >
                            <StatusBadge status={order.statusCode} />
                        </div>
                    ) : order.statusCode === 'WAITING_PO_CORRECTION' && onCorrectPoClick ? (
                        <div 
                            onClick={(e) => {
                                e.stopPropagation();
                                onCorrectPoClick();
                            }}
                            title="Corrigir P.O devolvida por Finanças"
                            style={{ 
                                cursor: 'pointer', 
                                display: 'inline-block',
                                transition: 'all 0.2s',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.opacity = '0.7'; e.currentTarget.style.transform = 'scale(1.02)'; }}
                            onMouseLeave={e => { e.currentTarget.style.opacity = '1'; e.currentTarget.style.transform = 'scale(1)'; }}
                        >
                            <StatusBadge status={order.statusCode} />
                        </div>
                    ) : (
                        <StatusBadge status={order.statusCode || 'DRAFT'} />
                    )}
                </div>
                <div style={{ textAlign: 'right', flexShrink: 0 }}>
                    <p style={{
                        fontSize: '0.6rem',
                        color: 'var(--color-text-muted)',
                        textTransform: 'uppercase',
                        fontWeight: 800,
                        letterSpacing: '0.02em',
                        margin: 0,
                    }}>Valor ({order.currencyCode || 'AOA'})</p>
                    <p style={{
                        fontWeight: 800,
                        color: 'var(--color-text-main)',
                        fontSize: '0.9rem',
                        margin: '2px 0 0',
                    }}>{(order.estimatedTotalAmount || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2 })}</p>
                </div>
            </div>
        </div>
    );
}

function MetaTag({ icon, text }: { icon: React.ReactNode; text: string }) {
    return (
        <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '4px',
            backgroundColor: 'var(--color-bg-page)',
            padding: '3px 8px',
            borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--color-border)',
        }}>
            <span style={{ color: 'var(--color-text-muted)', display: 'flex' }}>{icon}</span>
            <span style={{ maxWidth: '80px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{text}</span>
        </div>
    );
}
