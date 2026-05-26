import { useNavigate } from 'react-router-dom';
import { AttentionAlertDto } from '../../../types';

interface AlertListProps {
    alerts: AttentionAlertDto[];
}

const SEVERITY_STYLES: Record<string, { bg: string; border: string; color: string; icon: string }> = {
    CRITICAL: { bg: '#fef2f2', border: '#fecaca', color: '#dc2626', icon: '🔴' },
    WARNING: { bg: '#fffbeb', border: '#fde68a', color: '#d97706', icon: '🟠' },
    INFO: { bg: '#eff6ff', border: '#bfdbfe', color: '#2563eb', icon: '🔵' }
};

/**
 * "Atenção Requerida" — Structured alerts from the backend.
 * Never disappears — shows a clean empty state when there are no alerts.
 */
export function AlertList({ alerts }: AlertListProps) {
    const navigate = useNavigate();

    return (
        <section>
            <h2 style={{
                fontSize: '1.1rem',
                fontWeight: 700,
                color: 'var(--color-text)',
                margin: '0 0 16px 0'
            }}>
                Atenção Requerida
            </h2>

            {alerts.length === 0 ? (
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '12px',
                    padding: '32px',
                    textAlign: 'center',
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    gap: '8px'
                }}>
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#10b981" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
                        <polyline points="22 4 12 14.01 9 11.01" />
                    </svg>
                    <p style={{
                        margin: 0,
                        fontSize: '0.95rem',
                        fontWeight: 600,
                        color: '#10b981'
                    }}>
                        Nenhuma atenção crítica no momento.
                    </p>
                    <p style={{
                        margin: 0,
                        fontSize: '0.8rem',
                        color: 'var(--color-text-muted)'
                    }}>
                        Todos os pedidos estão dentro dos prazos esperados.
                    </p>
                </div>
            ) : (
                <div style={{
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '8px'
                }}>
                    {alerts.map(alert => {
                        const style = SEVERITY_STYLES[alert.severity] || SEVERITY_STYLES.INFO;

                        return (
                            <div
                                key={alert.id}
                                onClick={() => navigate(alert.targetPath)}
                                style={{
                                    backgroundColor: style.bg,
                                    border: `1px solid ${style.border}`,
                                    borderRadius: '10px',
                                    padding: '12px 16px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '12px',
                                    cursor: 'pointer',
                                    transition: 'all 0.15s'
                                }}
                                onMouseOver={(e) => {
                                    e.currentTarget.style.transform = 'translateX(4px)';
                                    e.currentTarget.style.boxShadow = '0 2px 8px rgba(0,0,0,0.08)';
                                }}
                                onMouseOut={(e) => {
                                    e.currentTarget.style.transform = 'none';
                                    e.currentTarget.style.boxShadow = 'none';
                                }}
                            >
                                {/* Severity indicator */}
                                <div style={{
                                    width: '4px',
                                    height: '36px',
                                    borderRadius: '2px',
                                    backgroundColor: style.color,
                                    flexShrink: 0
                                }} />

                                {/* Content */}
                                <div style={{ flex: 1, minWidth: 0 }}>
                                    <div style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: '8px',
                                        marginBottom: '2px'
                                    }}>
                                        <span style={{
                                            fontSize: '0.8rem',
                                            fontWeight: 700,
                                            color: style.color,
                                            fontVariantNumeric: 'tabular-nums'
                                        }}>
                                            {alert.requestNumber}
                                        </span>
                                        <span style={{
                                            fontSize: '0.75rem',
                                            padding: '1px 6px',
                                            borderRadius: '6px',
                                            backgroundColor: `${style.color}1A`,
                                            color: style.color,
                                            fontWeight: 600
                                        }}>
                                            {alert.reason}
                                        </span>
                                    </div>
                                    <div style={{
                                        fontSize: '0.8rem',
                                        color: 'var(--color-text-muted)',
                                        whiteSpace: 'nowrap',
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis'
                                    }}>
                                        {alert.title}
                                    </div>
                                </div>

                                {/* Status area */}
                                <div style={{
                                    fontSize: '0.7rem',
                                    color: 'var(--color-text-muted)',
                                    fontWeight: 500,
                                    textAlign: 'right',
                                    flexShrink: 0,
                                    whiteSpace: 'nowrap'
                                }}>
                                    {alert.responsibleArea}
                                </div>

                                {/* Arrow */}
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--color-text-muted)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0, opacity: 0.5 }}>
                                    <polyline points="9 18 15 12 9 6" />
                                </svg>
                            </div>
                        );
                    })}
                </div>
            )}
        </section>
    );
}
