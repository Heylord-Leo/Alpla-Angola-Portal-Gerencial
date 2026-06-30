import React from 'react';

export interface TimelineEvent {
    id: string | number;
    date: string; // ISO date string or formatted string
    user: string;
    actionType: string;
    description: string;
    icon?: React.ReactNode;
    beforeValue?: string;
    afterValue?: string;
    statusVariant?: 'green' | 'blue' | 'purple' | 'orange' | 'yellow' | 'gray' | 'red';
}

export interface AuditTimelineProps {
    events: TimelineEvent[];
    emptyMessage?: string;
    className?: string;
    style?: React.CSSProperties;
}

export function AuditTimeline({
    events,
    emptyMessage = 'Nenhum histórico encontrado.',
    className,
    style
}: AuditTimelineProps) {
    if (!events || events.length === 0) {
        return (
            <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.85rem', fontStyle: 'italic', ...style }}>
                {emptyMessage}
            </div>
        );
    }

    return (
        <div className={className} style={{ position: 'relative', paddingLeft: '16px', ...style }}>
            {/* Vertical Line */}
            <div style={{
                position: 'absolute',
                left: '23px',
                top: '16px',
                bottom: '16px',
                width: '2px',
                backgroundColor: 'var(--color-border)'
            }} />

            {events.map((event, index) => {
                const isLast = index === events.length - 1;
                return (
                    <div key={event.id} style={{ display: 'flex', gap: '16px', marginBottom: isLast ? 0 : '24px', position: 'relative' }}>
                        
                        {/* Dot / Icon */}
                        <div style={{
                            width: '16px',
                            height: '16px',
                            borderRadius: '50%',
                            backgroundColor: '#ffffff',
                            border: '2px solid #3b82f6',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            zIndex: 1,
                            marginTop: '4px',
                            boxShadow: '0 0 0 4px #ffffff'
                        }}>
                            {event.icon}
                        </div>

                        {/* Content */}
                        <div style={{ flex: 1, backgroundColor: '#f9fafb', borderRadius: '8px', padding: '12px', border: '1px solid var(--color-border)' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '8px' }}>
                                <div>
                                    <span style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)' }}>
                                        {event.actionType}
                                    </span>
                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                        por <span style={{ fontWeight: 500, color: 'var(--color-text)' }}>{event.user}</span>
                                    </div>
                                </div>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                    {event.date}
                                </span>
                            </div>

                            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--color-text)', lineHeight: 1.5 }}>
                                {event.description}
                            </p>

                            {(event.beforeValue || event.afterValue) && (
                                <div style={{ display: 'flex', gap: '12px', marginTop: '12px', fontSize: '0.8rem' }}>
                                    {event.beforeValue && (
                                        <div style={{ flex: 1, padding: '8px', backgroundColor: '#fef2f2', borderRadius: '6px', border: '1px solid #fecaca', color: '#991b1b' }}>
                                            <div style={{ fontSize: '0.7rem', fontWeight: 600, marginBottom: '2px', textTransform: 'uppercase' }}>Antes</div>
                                            {event.beforeValue}
                                        </div>
                                    )}
                                    {event.afterValue && (
                                        <div style={{ flex: 1, padding: '8px', backgroundColor: '#ecfdf5', borderRadius: '6px', border: '1px solid #a7f3d0', color: '#065f46' }}>
                                            <div style={{ fontSize: '0.7rem', fontWeight: 600, marginBottom: '2px', textTransform: 'uppercase' }}>Depois</div>
                                            {event.afterValue}
                                        </div>
                                    )}
                                </div>
                            )}
                        </div>
                    </div>
                );
            })}
        </div>
    );
}
