import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, Bell } from 'lucide-react';
import { AttentionPointDto } from '../../../types';

interface AttentionPanelProps {
    points: AttentionPointDto[];
}

export function AttentionPanel({ points }: AttentionPanelProps) {
    const navigate = useNavigate();

    if (points.length === 0) {
        return (
            <div style={{ 
                display: 'flex', 
                flexDirection: 'column', 
                alignItems: 'center', 
                justifyContent: 'center', 
                padding: '40px 20px',
                backgroundColor: 'rgba(var(--color-success-rgb), 0.05)',
                border: '2px dashed var(--color-status-success)',
                color: 'var(--color-status-success)',
                textAlign: 'center',
                gap: '12px'
            }}>
                <Bell size={40} strokeWidth={1.5} style={{ opacity: 0.5 }} />
                <div style={{ fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                    Tudo em conformidade
                </div>
                <div style={{ fontSize: '0.8rem', maxWidth: '300px' }}>
                    Nenhum pedido exige atenção imediata neste momento.
                </div>
            </div>
        );
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <h2 style={{ 
                    margin: 0, 
                    fontSize: '1.2rem', 
                    fontWeight: 800, 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    color: 'var(--color-primary)',
                    borderLeft: '4px solid var(--color-primary)',
                    paddingLeft: '12px'
                }}>
                    Pontos de Atenção
                </h2>
                <span style={{ 
                    backgroundColor: 'var(--color-status-danger)', 
                    color: '#fff', 
                    padding: '2px 8px', 
                    fontSize: '0.75rem', 
                    fontWeight: 900,
                    borderRadius: '2px'
                    }}>
                    {points.length} ALERTAS
                </span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {points.map(point => (
                    <button
                        key={point.id}
                        onClick={() => navigate(point.targetPath)}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '16px',
                            padding: '16px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderLeft: `8px solid ${
                                point.type === 'DANGER' ? 'var(--color-status-danger)' :
                                point.type === 'WARNING' ? 'var(--color-status-warning)' :
                                point.type === 'SUCCESS' ? 'var(--color-status-success)' :
                                'var(--color-status-info)'
                            }`,
                            cursor: 'pointer',
                            textAlign: 'left',
                            transition: 'all 0.1s ease',
                            boxShadow: '2px 2px 0px var(--color-border)',
                        }}
                        onMouseOver={(e) => {
                            e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.02)';
                        }}
                        onMouseOut={(e) => {
                            e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)';
                        }}
                    >
                        <div style={{ 
                            color: point.type === 'DANGER' ? 'var(--color-status-danger)' :
                                   point.type === 'WARNING' ? 'var(--color-status-warning)' :
                                   'var(--color-primary)',
                            display: 'flex'
                        }}>
                            <AlertCircle size={28} strokeWidth={2.5} />
                        </div>
                        <div style={{ flex: 1 }}>
                            <div style={{ 
                                display: 'flex', 
                                alignItems: 'center', 
                                gap: '8px',
                                fontWeight: 800, 
                                fontSize: '0.9rem', 
                                textTransform: 'uppercase', 
                                color: 'var(--color-text-main)' 
                            }}>
                                {point.title}
                                <span style={{ 
                                    opacity: 0.3, 
                                    fontSize: '1.1rem',
                                    fontWeight: 400
                                }}>•</span>
                                <span style={{ color: point.type === 'DANGER' ? 'var(--color-status-danger)' : 'var(--color-primary)' }}>
                                    {point.count} itens
                                </span>
                            </div>
                            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                {point.description}
                            </div>
                        </div>
                        <div style={{ color: 'var(--color-text-muted)' }}>
                            <ChevronRight size={20} />
                        </div>
                    </button>
                ))}
            </div>
        </div>
    );
}
