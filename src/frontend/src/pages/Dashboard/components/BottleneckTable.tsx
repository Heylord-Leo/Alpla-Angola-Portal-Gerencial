import { StageBottleneckDto } from '../../../types';
import { formatDate } from '../../../lib/utils';

interface BottleneckTableProps {
    bottlenecks: StageBottleneckDto[];
}

/**
 * "Gargalos do Processo" — Shows which workflow stages have the most requests stuck.
 * Ordered by count (descending) to highlight the worst bottleneck.
 */
export function BottleneckTable({ bottlenecks }: BottleneckTableProps) {
    if (bottlenecks.length === 0) {
        return (
            <section>
                <h2 style={{
                    fontSize: '1.1rem',
                    fontWeight: 700,
                    color: 'var(--color-text)',
                    margin: '0 0 16px 0'
                }}>
                    Gargalos do Processo
                </h2>
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '12px',
                    padding: '32px',
                    textAlign: 'center',
                    color: 'var(--color-text-muted)'
                }}>
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#10b981" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ marginBottom: '8px' }}>
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
                        <polyline points="22 4 12 14.01 9 11.01" />
                    </svg>
                    <p style={{ margin: 0, fontSize: '0.9rem', fontWeight: 500 }}>Nenhum gargalo significativo no momento.</p>
                </div>
            </section>
        );
    }

    const maxCount = bottlenecks[0]?.count ?? 1;

    // Calculate age in days
    function getAgeDays(oldestDate: string | null): number | null {
        if (!oldestDate) return null;
        const d = new Date(oldestDate);
        if (isNaN(d.getTime())) return null;
        const today = new Date();
        return Math.floor((today.getTime() - d.getTime()) / (1000 * 60 * 60 * 24));
    }

    function getUrgencyColor(ageDays: number | null): string {
        if (ageDays === null) return 'var(--color-text-muted)';
        if (ageDays > 14) return '#ef4444';  // critical
        if (ageDays > 7) return '#f97316';   // warning
        if (ageDays > 3) return '#eab308';   // attention
        return '#10b981';                     // healthy
    }

    return (
        <section>
            <h2 style={{
                fontSize: '1.1rem',
                fontWeight: 700,
                color: 'var(--color-text)',
                margin: '0 0 16px 0'
            }}>
                Gargalos do Processo
            </h2>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                borderRadius: '12px',
                overflow: 'hidden'
            }}>
                <table style={{
                    width: '100%',
                    borderCollapse: 'collapse',
                    fontSize: '0.875rem'
                }}>
                    <thead>
                        <tr style={{
                            borderBottom: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-elevated, var(--color-bg-surface))'
                        }}>
                            <th style={{ textAlign: 'left', padding: '12px 16px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Etapa</th>
                            <th style={{ textAlign: 'right', padding: '12px 16px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Qtd.</th>
                            <th style={{ textAlign: 'left', padding: '12px 16px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em', minWidth: '120px' }}>Distribuição</th>
                            <th style={{ textAlign: 'left', padding: '12px 16px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Mais Antigo</th>
                            <th style={{ textAlign: 'center', padding: '12px 16px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Idade</th>
                        </tr>
                    </thead>
                    <tbody>
                        {bottlenecks.map((b, i) => {
                            const ageDays = getAgeDays(b.oldestCreatedAtUtc);
                            const urgencyColor = getUrgencyColor(ageDays);
                            const barWidth = maxCount > 0 ? (b.count / maxCount) * 100 : 0;

                            return (
                                <tr
                                    key={b.stageCode}
                                    style={{
                                        borderBottom: i < bottlenecks.length - 1 ? '1px solid var(--color-border)' : 'none',
                                        transition: 'background-color 0.15s'
                                    }}
                                    onMouseOver={(e) => e.currentTarget.style.backgroundColor = 'rgba(56, 189, 248, 0.04)'}
                                    onMouseOut={(e) => e.currentTarget.style.backgroundColor = ''}
                                >
                                    <td style={{ padding: '12px 16px', fontWeight: 500, color: 'var(--color-text)' }}>
                                        {b.stageName}
                                    </td>
                                    <td style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 700, color: 'var(--color-text)', fontVariantNumeric: 'tabular-nums' }}>
                                        {b.count}
                                    </td>
                                    <td style={{ padding: '12px 16px' }}>
                                        <div style={{
                                            backgroundColor: 'var(--color-border)',
                                            borderRadius: '4px',
                                            height: '8px',
                                            width: '100%',
                                            overflow: 'hidden'
                                        }}>
                                            <div style={{
                                                width: `${barWidth}%`,
                                                height: '100%',
                                                backgroundColor: urgencyColor,
                                                borderRadius: '4px',
                                                transition: 'width 0.6s ease-out'
                                            }} />
                                        </div>
                                    </td>
                                    <td style={{ padding: '12px 16px', color: 'var(--color-text-muted)', fontVariantNumeric: 'tabular-nums' }}>
                                        {formatDate(b.oldestCreatedAtUtc)}
                                    </td>
                                    <td style={{ padding: '12px 16px', textAlign: 'center' }}>
                                        {ageDays !== null ? (
                                            <span style={{
                                                display: 'inline-flex',
                                                alignItems: 'center',
                                                gap: '4px',
                                                padding: '2px 8px',
                                                borderRadius: '12px',
                                                fontSize: '0.75rem',
                                                fontWeight: 600,
                                                backgroundColor: `${urgencyColor}1A`,
                                                color: urgencyColor
                                            }}>
                                                {ageDays}d
                                            </span>
                                        ) : (
                                            <span style={{ color: 'var(--color-text-muted)' }}>—</span>
                                        )}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
        </section>
    );
}
