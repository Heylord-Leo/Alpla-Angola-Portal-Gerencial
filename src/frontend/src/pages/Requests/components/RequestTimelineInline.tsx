import React, { useEffect, useState } from 'react';
import { formatDate, formatTime } from '../../../lib/utils';
import { api } from '../../../lib/api';
import { RequestTimelineDto } from '../../../types';
import { Check, Loader2, X, Flag } from 'lucide-react';

interface RequestTimelineInlineProps {
    requestId: string;
}

export const RequestTimelineInline: React.FC<RequestTimelineInlineProps> = ({ requestId }) => {
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
                <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                    Carregando progresso...
                </span>
            </div>
        );
    }

    if (error) {
        return (
            <div style={{ padding: '24px 0', textAlign: 'center', color: 'var(--color-status-rejected)', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>
                {error}
            </div>
        );
    }

    if (!timeline || !timeline.steps || timeline.steps.length === 0) {
        return (
            <div style={{ padding: '24px 0', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.8rem', fontStyle: 'italic', fontWeight: 600 }}>
                Fluxo de trabalho não disponível para este pedido.
            </div>
        );
    }

    return (
        <div style={{ width: '100%', overflowX: 'auto', paddingBottom: '16px' }}>
            <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', minWidth: '850px', padding: '24px 32px 0' }}>
                {timeline.steps.map((step, index) => {
                    const isCompleted = step.state === 'completed';
                    const isCurrent = step.state === 'current';
                    const isBlocked = step.state === 'blocked';
                    const isLast = index === timeline.steps.length - 1;

                    // Connector logic
                    const nextStep = !isLast ? timeline.steps[index + 1] : null;
                    const connectorActive = isCompleted && (nextStep?.state === 'completed' || nextStep?.state === 'current');

                    return (
                        <div key={index} style={{ flex: isLast ? '0 0 auto' : '1 1 0%', display: 'flex', flexDirection: 'column', alignItems: 'center', position: 'relative', minWidth: 0 }}>
                            {/* Marker Row - Exact visual axis for markers and connectors */}
                            <div style={{ width: '100%', height: '40px', display: 'flex', justifyContent: 'center', alignItems: 'center', position: 'relative' }}>
                                {/* Connector Line - Starts at marker center (50%) and spans 100% of width to reach next center */}
                                {!isLast && (
                                    <div style={{
                                        position: 'absolute',
                                        left: '50%',
                                        width: '100%',
                                        top: '20px',
                                        marginTop: '-1px',
                                        height: '2px',
                                        backgroundColor: connectorActive ? 'var(--color-primary)' : 'var(--color-bg-page)',
                                        border: connectorActive ? 'none' : '1px solid var(--color-border)',
                                        borderRadius: '4px',
                                        zIndex: 1,
                                        transition: 'all 0.5s ease'
                                    }} />
                                )}

                                {/* Marker Circle */}
                                <div style={{
                                    position: 'relative',
                                    zIndex: 2,
                                    width: '40px',
                                    height: '40px',
                                    borderRadius: '50%',
                                    // Current has border, Completed is solid blue, Blocked is red X, Future is gray border
                                    border: isCurrent
                                        ? '3px solid var(--color-primary)'
                                        : isCompleted
                                            ? 'none'
                                            : isBlocked
                                                ? '2px solid var(--color-status-rejected)'
                                                : '2px solid var(--color-border)',
                                    backgroundColor: isCompleted ? 'var(--color-primary)' : isBlocked ? 'rgba(var(--color-status-rejected-rgb), 0.05)' : '#fff',
                                    color: isCompleted ? '#fff' : isCurrent ? 'var(--color-primary)' : isBlocked ? 'var(--color-status-rejected)' : 'var(--color-text-muted)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    flexShrink: 0,
                                    boxShadow: isCurrent ? '0 0 15px rgba(var(--color-primary-rgb), 0.3)' : 'none',
                                    transition: 'all 0.3s ease',
                                    boxSizing: 'border-box'
                                }}>
                                    {isCompleted ? (
                                        isLast ? <Flag size={20} strokeWidth={4} fill="currentColor" /> : <Check size={20} strokeWidth={4} />
                                    ) : isBlocked ? (
                                        <X size={20} strokeWidth={4} />
                                    ) : (
                                        isLast && isCurrent ? <Flag size={20} strokeWidth={4} fill="currentColor" /> : <span style={{ fontSize: '0.8rem', fontWeight: 900 }}>{index + 1}</span>
                                    )}
                                </div>
                            </div>

                            {/* Label Container - Tightened spacing */}
                            <div style={{
                                marginTop: '4px',
                                width: '140px',
                                textAlign: 'center',
                                display: 'flex',
                                flexDirection: 'column',
                                alignItems: 'center'
                            }}>
                                <span style={{
                                    fontSize: '0.65rem',
                                    fontWeight: 900,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.02em',
                                    lineHeight: '1.2',
                                    color: isCurrent ? 'var(--color-primary)' : isCompleted ? 'var(--color-text-main)' : isBlocked ? 'var(--color-status-rejected)' : 'var(--color-text-muted)',
                                    opacity: isBlocked ? 0.7 : 1,
                                    marginBottom: '4px'
                                }}>
                                    {step.label}
                                </span>

                                {step.completedAt && (
                                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', opacity: 0.8, marginTop: '2px' }}>
                                        <span style={{ fontSize: '0.6rem', color: 'var(--color-text-main)', fontWeight: 800, fontFamily: 'monospace' }}>
                                            {formatDate(step.completedAt)}
                                        </span>
                                        <span style={{ fontSize: '0.55rem', color: 'var(--color-text-muted)', fontWeight: 600, fontFamily: 'monospace' }}>
                                            {formatTime(step.completedAt)}
                                        </span>
                                    </div>
                                )}

                                {isCurrent && (
                                    <div style={{
                                        marginTop: '6px',
                                        padding: '2px 6px',
                                        backgroundColor: 'var(--color-primary)',
                                        color: '#fff',
                                        fontSize: '0.55rem',
                                        fontWeight: 900,
                                        borderRadius: '2px',
                                        letterSpacing: '0.05em'
                                    }}>
                                        ATUAL
                                    </div>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>

            <style dangerouslySetInnerHTML={{
                __html: `
@keyframes spin {
                    from { transform: rotate(0deg); }
                    to { transform: rotate(360deg); }
}
`}} />
        </div>
    );
};
