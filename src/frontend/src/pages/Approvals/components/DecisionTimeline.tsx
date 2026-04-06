import { formatDateTime } from '../../../lib/utils';
import { User, MessageCircle } from 'lucide-react';

interface TimelineEntry {
    id: string;
    actionTaken: string;
    newStatusName: string;
    actorName: string;
    createdAtUtc: string;
    comment?: string | null;
}

interface DecisionTimelineProps {
    entries: TimelineEntry[];
}

export function DecisionTimeline({ entries }: DecisionTimelineProps) {
    if (!entries || entries.length === 0) {
        return (
            <div style={{ 
                padding: '24px', 
                textAlign: 'center', 
                color: 'var(--color-text-muted)', 
                fontWeight: 600,
                fontSize: '0.8rem'
            }}>
                Nenhum histórico disponível.
            </div>
        );
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column' }}>
            {entries.map((entry, idx) => (
                <div key={entry.id} style={{ 
                    display: 'flex', 
                    gap: '16px',
                    position: 'relative',
                    paddingBottom: idx < entries.length - 1 ? '24px' : '0'
                }}>
                    {/* --- Timeline Line --- */}
                    {idx < entries.length - 1 && (
                        <div style={{
                            position: 'absolute',
                            left: '11px',
                            top: '24px',
                            bottom: 0,
                            width: '2px',
                            backgroundColor: 'var(--color-border)',
                            opacity: 1
                        }} />
                    )}

                    {/* --- Marker --- */}
                    <div style={{
                        width: '24px',
                        height: '24px',
                        borderRadius: '50%',
                        backgroundColor: 'var(--color-text-main)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: 'white',
                        flexShrink: 0,
                        zIndex: 1
                    }}>
                        <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: 'white' }} />
                    </div>

                    {/* --- Content --- */}
                    <div style={{ flex: 1 }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px', marginBottom: '4px' }}>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center' }}>
                                <span style={{ fontWeight: 900, fontSize: '0.75rem', textTransform: 'uppercase' }}>
                                    {entry.actionTaken}
                                </span>
                                <span style={{ fontWeight: 800, fontSize: '10px', color: 'var(--color-text-muted)' }}>
                                    → {entry.newStatusName}
                                </span>
                            </div>
                            <span style={{ fontSize: '10px', fontWeight: 800, color: 'var(--color-text-muted)' }}>
                                {formatDateTime(entry.createdAtUtc)}
                            </span>
                        </div>
                        
                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--color-text-main)', fontSize: '0.75rem', fontWeight: 600 }}>
                            <User size={12} /> {entry.actorName}
                        </div>
                        
                        {entry.comment && (
                            <div style={{
                                marginTop: '8px',
                                padding: '12px 14px',
                                backgroundColor: 'var(--color-bg-surface)',
                                borderLeft: '4px solid var(--color-border-heavy)',
                                borderRadius: '0 var(--radius-md) var(--radius-md) 0',
                                border: '1px solid var(--color-border)',
                                fontSize: '0.8rem',
                                fontStyle: 'italic',
                                fontWeight: 500,
                                color: 'var(--color-text-main)',
                                display: 'flex',
                                gap: '10px'
                            }}>
                                <MessageCircle size={14} style={{ flexShrink: 0, marginTop: '2px', opacity: 0.5 }} />
                                "{entry.comment}"
                            </div>
                        )}
                    </div>
                </div>
            ))}
        </div>
    );
}
