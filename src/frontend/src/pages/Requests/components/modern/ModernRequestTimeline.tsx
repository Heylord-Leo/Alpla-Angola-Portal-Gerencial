import { useEffect, useState } from 'react';
import { motion, Variants } from 'framer-motion';
import { Check, ChevronDown, ChevronUp, Loader2, X } from 'lucide-react';
import { api } from '../../../../lib/api';
import { RequestTimelineDto, LotTimelineDto, TimelineStepDto } from '../../../../types';
import { formatDateAngola, formatTimeAngola } from '../../../../lib/utils';
import { defaultExpandedLotIndex, lotHeaderTitle } from '../../../../lib/workflowProjection';

interface ModernRequestTimelineProps {
    requestId: string;
}

const containerVariants: Variants = {
    hidden: { opacity: 0 },
    visible: {
        opacity: 1,
        transition: { staggerChildren: 0.15 }
    }
};

const circleVariants: Variants = {
    hidden: { scale: 0, opacity: 0 },
    visible: {
        scale: 1,
        opacity: 1,
        transition: { type: 'spring', stiffness: 300, damping: 20 }
    }
};

export function ModernRequestTimeline({ requestId }: ModernRequestTimelineProps) {
    const [timeline, setTimeline] = useState<RequestTimelineDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchTimeline = async () => {
            try {
                setLoading(true);
                const data = await api.requests.getTimeline(requestId);
                setTimeline(data);
            } catch (err: any) {
                setError(err.message || 'Erro ao carregar timeline');
            } finally {
                setLoading(false);
            }
        };

        fetchTimeline();
    }, [requestId]);

    if (loading) {
        return (
            <div style={{ padding: '32px 0', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '12px' }}>
                <Loader2 size={20} style={{ animation: 'spin 1s linear infinite', color: 'var(--color-primary)' }} />
                <span style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>Carregando histórico...</span>
            </div>
        );
    }

    if (error || !timeline || !timeline.steps || timeline.steps.length === 0) {
        return (
            <div style={{ padding: '24px 0', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.85rem', fontStyle: 'italic' }}>
                {error || 'Histórico não disponível.'}
            </div>
        );
    }

    // v2.230.0 historical compatibility: ANY reconstructible operational unit renders the
    // unit-based timeline (single lot = one slim header + one track, no accordion). Only
    // unit-less legacy requests keep the Request-level timeline below.
    const lots = timeline.lots ?? [];
    if (lots.length >= 1) {
        return <LotTimelines lots={lots} />;
    }

    return (
        <div style={{
            width: '100%',
            overflowX: 'auto',
            padding: '24px 32px 32px',
        }}>
            <TimelineTrack steps={timeline.steps} />
        </div>
    );
}

/** The existing horizontal timeline track, parametrized so multi-lot rows reuse the exact visual language. */
function TimelineTrack({ steps }: { steps: TimelineStepDto[] }) {
    return (
            <motion.div
                variants={containerVariants}
                initial="hidden"
                animate="visible"
                style={{
                    display: 'flex',
                    alignItems: 'flex-start',
                    justifyContent: 'space-between',
                    minWidth: '850px',
                    position: 'relative'
                }}
            >
                {/* Background Track Line */}
                <div style={{
                    position: 'absolute',
                    top: '20px',
                    left: 0,
                    right: 0,
                    height: '2px',
                    backgroundColor: 'var(--color-border)',
                    zIndex: 0,
                    marginLeft: '40px',
                    marginRight: '40px'
                }} />

                {/* Animated Growing Line for Completeness */}
                <motion.div style={{
                    position: 'absolute',
                    top: '20px',
                    left: 0,
                    right: 0,
                    height: '2px',
                    backgroundColor: 'var(--color-primary)',
                    zIndex: 1,
                    transformOrigin: 'left',
                    marginLeft: '40px',
                    marginRight: '40px'
                }}
                initial={{ scaleX: 0 }}
                animate={{ scaleX: steps.filter(s => s.state === 'completed' || s.state === 'current').length / steps.length }}
                transition={{ duration: 0.8, ease: "easeOut" }}
                />

                {steps.map((step, index) => {
                    const isCompleted = step.state === 'completed';
                    const isCurrent = step.state === 'current';
                    const isBlocked = step.state === 'blocked';
                    const isSkipped = step.state === 'skipped';
                    const isLast = index === steps.length - 1;

                    return (
                        <div key={index} style={{
                            flex: isLast ? '0 0 auto' : '1 1 0%',
                            display: 'flex',
                            flexDirection: 'column',
                            alignItems: 'center',
                            position: 'relative',
                            zIndex: 2
                        }}>
                            {/* Marker Circle */}
                            <motion.div
                                variants={circleVariants}
                                title={isSkipped ? 'Etapa não realizada — pedido encerrado sem cotação.' : undefined}
                                style={{
                                    width: '40px',
                                    height: '40px',
                                    borderRadius: '50%',
                                    backgroundColor: isCompleted ? 'var(--color-primary)' : '#fff',
                                    border: isCurrent ? '2px solid var(--color-primary)' : isCompleted ? 'none' : isSkipped ? '2px dashed var(--color-border)' : '2px solid var(--color-border)',
                                    color: isCompleted ? '#fff' : isCurrent ? 'var(--color-primary)' : 'var(--color-text-muted)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    transform: isCurrent ? 'scale(1.15)' : 'scale(1)',
                                    boxShadow: isCurrent ? '0 4px 12px rgba(var(--color-primary-rgb), 0.2)' : 'none',
                                    transition: 'all 0.3s ease',
                                    position: 'relative'
                                }}
                            >
                                {isCompleted ? (
                                    <Check size={20} strokeWidth={4} />
                                ) : isBlocked ? (
                                    <X size={20} strokeWidth={4} style={{ color: 'var(--color-status-red)' }} />
                                ) : isSkipped ? (
                                    <X size={18} strokeWidth={3} style={{ color: 'var(--color-text-muted)' }} />
                                ) : (
                                    <span style={{ fontSize: isCurrent ? '0.9rem' : '0.8rem', fontWeight: 900 }}>{index + 1}</span>
                                )}
                            </motion.div>

                            {/* Label */}
                            <motion.div
                                variants={containerVariants}
                                style={{
                                    marginTop: isCurrent ? '12px' : '8px',
                                    textAlign: 'center',
                                    width: '120px'
                                }}
                            >
                                <div style={{
                                    fontSize: '0.7rem',
                                    fontWeight: 800,
                                    textTransform: 'uppercase',
                                    color: isCurrent ? 'var(--color-primary)' : isCompleted ? 'var(--color-text-main)' : 'var(--color-text-muted)',
                                    opacity: (isBlocked || isSkipped) ? 0.7 : 1,
                                    lineHeight: 1.2,
                                    marginBottom: '4px'
                                }}>
                                    {step.label}
                                </div>
                                {isSkipped && (
                                    <span
                                        title="Etapa não realizada — pedido encerrado sem cotação."
                                        style={{
                                            display: 'inline-block',
                                            backgroundColor: 'var(--color-bg-page)',
                                            border: '1px solid var(--color-border)',
                                            color: 'var(--color-text-muted)',
                                            padding: '2px 8px',
                                            borderRadius: 'var(--radius-full)',
                                            fontSize: '0.6rem',
                                            fontWeight: 800,
                                            letterSpacing: '0.05em',
                                            textTransform: 'uppercase',
                                            cursor: 'help'
                                        }}
                                    >
                                        Não aplicável
                                    </span>
                                )}
                                {step.completedAt && (
                                    <div style={{ display: 'flex', flexDirection: 'column', opacity: 0.7 }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 700, fontFamily: 'monospace' }}>
                                            {formatDateAngola(step.completedAt)}
                                        </span>
                                        <span style={{ fontSize: '0.6rem', fontWeight: 600, fontFamily: 'monospace' }}>
                                            {formatTimeAngola(step.completedAt)}
                                        </span>
                                    </div>
                                )}
                                
                                {/* ATUAL Tag */}
                                {isCurrent && (
                                    <motion.div
                                        initial={{ opacity: 0, y: -5 }}
                                        animate={{ opacity: 1, y: 0 }}
                                        transition={{ delay: 0.4, type: 'spring' }}
                                        style={{
                                            marginTop: '6px',
                                            display: 'inline-block',
                                            backgroundColor: 'var(--color-primary)',
                                            color: '#fff',
                                            padding: '2px 8px',
                                            borderRadius: 'var(--radius-full)',
                                            fontSize: '0.6rem',
                                            fontWeight: 900,
                                            letterSpacing: '0.05em',
                                            textTransform: 'uppercase'
                                        }}
                                    >
                                        ATUAL
                                    </motion.div>
                                )}
                            </motion.div>
                        </div>
                    );
                })}
            </motion.div>
    );
}

/**
 * v2.230.0 — one compact section per logical lot inside the expanded Requests-list row.
 * ≤ 3 lots: all tracks expanded. > 3 lots: collapsible sections, all headers visible, only the
 * first lot that still requires work starts expanded (a completed Lote #1 never dominates).
 */
function LotTimelines({ lots }: { lots: LotTimelineDto[] }) {
    const collapsible = lots.length > 3;
    const [expanded, setExpanded] = useState<Set<number>>(() =>
        collapsible ? new Set([defaultExpandedLotIndex(lots)]) : new Set(lots.map((_, i) => i)));

    const toggle = (i: number) => setExpanded(prev => {
        const next = new Set(prev);
        if (next.has(i)) next.delete(i); else next.add(i);
        return next;
    });

    return (
        <div style={{ width: '100%', padding: '16px 32px 24px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
            {lots.map((lot, i) => {
                const isOpen = expanded.has(i);
                return (
                    <div key={lot.unitId} style={{ borderTop: i > 0 ? '1px dashed var(--color-border)' : 'none', paddingTop: i > 0 ? '10px' : 0 }}>
                        {/* Lot header — real domain identity only (never a fabricated lot number) */}
                        <div
                            onClick={collapsible ? () => toggle(i) : undefined}
                            role={collapsible ? 'button' : undefined}
                            aria-expanded={collapsible ? isOpen : undefined}
                            style={{
                                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
                                cursor: collapsible ? 'pointer' : 'default', padding: '4px 0'
                            }}
                        >
                            <div style={{ minWidth: 0, display: 'flex', alignItems: 'baseline', gap: '10px', flexWrap: 'wrap' }}>
                                <span style={{ fontSize: '0.78rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                    {lotHeaderTitle(lot)}
                                </span>
                                {lot.totalAmount > 0 && (
                                    <span style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                                        {lot.currencyCode || 'AOA'} {lot.totalAmount.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}
                                    </span>
                                )}
                                {lot.purchaseOrderNumber && (
                                    <span style={{ fontSize: '0.7rem', fontWeight: 700, fontFamily: 'monospace', color: 'var(--color-text-muted)' }}>
                                        P.O. {lot.purchaseOrderNumber}
                                    </span>
                                )}
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                                <span style={{
                                    fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase',
                                    padding: '3px 8px', borderRadius: '999px', whiteSpace: 'nowrap',
                                    backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
                                    color: 'var(--color-primary)'
                                }}>
                                    {lot.statusLabel}
                                </span>
                                {collapsible && (isOpen ? <ChevronUp size={14} /> : <ChevronDown size={14} />)}
                            </div>
                        </div>

                        {isOpen && (
                            <div style={{ width: '100%', overflowX: 'auto', padding: '12px 0 8px' }}>
                                <TimelineTrack steps={lot.steps} />
                            </div>
                        )}
                    </div>
                );
            })}
        </div>
    );
}
