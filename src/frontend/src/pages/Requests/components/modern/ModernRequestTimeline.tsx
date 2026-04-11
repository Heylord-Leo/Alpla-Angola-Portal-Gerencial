import React, { useEffect, useState } from 'react';
import { motion, Variants } from 'framer-motion';
import { Check, Loader2, X, Flag } from 'lucide-react';
import { api } from '../../../../lib/api';
import { RequestTimelineDto } from '../../../../types';
import { formatDate, formatTime } from '../../../../lib/utils';

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
                <Loader2 size={20} className="animate-spin" style={{ color: 'var(--color-primary)' }} />
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

    return (
        <div style={{
            width: '100%',
            overflowX: 'auto',
            padding: '24px 32px 32px',
        }}>
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
                animate={{ scaleX: timeline.steps.filter(s => s.state === 'completed' || s.state === 'current').length / timeline.steps.length }}
                transition={{ duration: 0.8, ease: "easeOut" }}
                />

                {timeline.steps.map((step, index) => {
                    const isCompleted = step.state === 'completed';
                    const isCurrent = step.state === 'current';
                    const isBlocked = step.state === 'blocked';
                    const isLast = index === timeline.steps.length - 1;

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
                                style={{
                                    width: '40px',
                                    height: '40px',
                                    borderRadius: '50%',
                                    backgroundColor: isCompleted ? 'var(--color-primary)' : '#fff',
                                    border: isCurrent ? '2px solid var(--color-primary)' : isCompleted ? 'none' : '2px solid var(--color-border)',
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
                                    opacity: isBlocked ? 0.7 : 1,
                                    lineHeight: 1.2,
                                    marginBottom: '4px'
                                }}>
                                    {step.label}
                                </div>
                                {step.completedAt && (
                                    <div style={{ display: 'flex', flexDirection: 'column', opacity: 0.7 }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 700, fontFamily: 'monospace' }}>
                                            {formatDate(step.completedAt)}
                                        </span>
                                        <span style={{ fontSize: '0.6rem', fontWeight: 600, fontFamily: 'monospace' }}>
                                            {formatTime(step.completedAt)}
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
        </div>
    );
}
