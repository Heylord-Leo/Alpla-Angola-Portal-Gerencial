import React from 'react';
import { FinancialByStatusDto } from '../../../types';
import { formatCurrencyAO } from '../../../lib/utils';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';

interface FinancialSummaryProps {
    data: FinancialByStatusDto[];
}

const GROUP_COLORS: Record<string, string> = {
    'Em Aprovação': '#3b82f6',
    'Aprovado / Ag. PO': '#8b5cf6',
    'Pendente Pagamento': '#f97316',
    'Pago / Finalizado': '#10b981',
};

const GROUP_ICONS: Record<string, React.ReactNode> = {
    'Em Aprovação': (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" />
        </svg>
    ),
    'Aprovado / Ag. PO': (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" /><polyline points="22 4 12 14.01 9 11.01" />
        </svg>
    ),
    'Pendente Pagamento': (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="1" y="4" width="22" height="16" rx="2" ry="2" /><line x1="1" y1="10" x2="23" y2="10" />
        </svg>
    ),
    'Pago / Finalizado': (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6" />
        </svg>
    ),
};

/**
 * "Resumo Financeiro" — Financial cards grouped by status, using only reliable data from the backend.
 * Shows no fake data — empty state when no financial metrics available.
 */
export function FinancialSummary({ data }: FinancialSummaryProps) {
    if (data.length === 0) {
        return (
            <section>
                <h2 style={{
                    fontSize: '1.1rem',
                    fontWeight: 700,
                    color: 'var(--color-text)',
                    margin: '0 0 16px 0'
                }}>
                    Resumo Financeiro
                </h2>
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '12px',
                    padding: '32px',
                    textAlign: 'center',
                    color: 'var(--color-text-muted)'
                }}>
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="var(--color-text-muted)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ marginBottom: '8px', opacity: 0.5 }}>
                        <line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6" />
                    </svg>
                    <p style={{ margin: 0, fontSize: '0.9rem', fontWeight: 500 }}>Sem dados financeiros disponíveis no momento.</p>
                </div>
            </section>
        );
    }

    return (
        <section>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, margin: '0 0 16px 0' }}>
                <h2 style={{
                    fontSize: '1.1rem',
                    fontWeight: 700,
                    color: 'var(--color-text-main)',
                    margin: 0
                }}>
                    Resumo Financeiro
                </h2>
                <SectionInfo {...DASHBOARD_SECTION_HELP.financialSummary} />
            </div>
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
                gap: '16px'
            }}>
                {data.map(group => {
                    const color = GROUP_COLORS[group.groupLabel] || '#6b7280';
                    const icon = GROUP_ICONS[group.groupLabel];
                    const isMultiCurrency = group.currencyCodes.length > 1;
                    const mainCurrency = group.currencyCodes.length === 1 ? group.currencyCodes[0] : undefined;

                    return (
                        <div
                            key={group.groupLabel}
                            style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-border)',
                                borderRadius: '12px',
                                padding: '20px',
                                position: 'relative',
                                overflow: 'hidden'
                            }}
                        >
                            {/* Color accent bar */}
                            <div style={{
                                position: 'absolute',
                                top: 0,
                                left: 0,
                                right: 0,
                                height: '3px',
                                backgroundColor: color
                            }} />

                            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '12px' }}>
                                <span style={{
                                    fontSize: '0.75rem',
                                    fontWeight: 600,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.05em',
                                    color: color
                                }}>
                                    {group.groupLabel}
                                </span>
                                {icon && (
                                    <div style={{
                                        width: 32,
                                        height: 32,
                                        backgroundColor: `${color}1A`,
                                        color: color,
                                        borderRadius: '8px',
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center'
                                    }}>
                                        {icon}
                                    </div>
                                )}
                            </div>

                            <div style={{
                                fontSize: '1.5rem',
                                fontWeight: 700,
                                color: 'var(--color-text)',
                                marginBottom: '4px',
                                fontVariantNumeric: 'tabular-nums'
                            }}>
                                {isMultiCurrency ? (
                                    <span style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
                                        Multi-moeda
                                    </span>
                                ) : (
                                    formatCurrencyAO(group.totalAmount, mainCurrency)
                                )}
                            </div>

                            <div style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                fontSize: '0.8rem',
                                color: 'var(--color-text-muted)',
                                fontWeight: 500
                            }}>
                                <span>{group.count} pedido{group.count !== 1 ? 's' : ''}</span>
                                {isMultiCurrency && (
                                    <span style={{
                                        fontSize: '0.7rem',
                                        padding: '1px 6px',
                                        borderRadius: '8px',
                                        backgroundColor: '#fef3c7',
                                        color: '#92400e',
                                        fontWeight: 600
                                    }}>
                                        {group.currencyCodes.join(', ')}
                                    </span>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        </section>
    );
}
