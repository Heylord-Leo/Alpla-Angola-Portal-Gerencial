import { ArrowLeft, MoreVertical } from 'lucide-react';
import { StatusBadge } from '../../../components/ui/StatusBadge';
import { formatCurrencyAO } from '../../../lib/utils';

export interface DecisionHeaderProps {
    requestNumber: string;
    requestTypeCode: string;
    statusCode: string;
    statusName: string;
    statusBadgeColor: string;
    totalAmount: number;
    currencyCode: string;
    approvalStage: 'AREA' | 'FINAL';
    onClose: () => void;
    onOpenRequest: () => void;
    onNext?: () => void;
    onPrev?: () => void;
    currentIndex?: number;
    totalCount?: number;
}

export function DecisionHeader({
    requestNumber,
    requestTypeCode,
    statusCode,
    statusName,
    statusBadgeColor,
    totalAmount,
    currencyCode,
    onClose,
    onOpenRequest,
    onNext,
    onPrev,
    currentIndex,
    totalCount
}: DecisionHeaderProps) {
    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)', /* or page if we want soft */
            display: 'flex', flexDirection: 'column', alignItems: 'center',
            paddingBottom: '32px',
            borderBottom: '1px solid var(--color-border)'
        }}>
            {/* Top Navigation Bar */}
            <div style={{
                width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                padding: '16px 24px'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <button 
                        onClick={onClose} 
                        style={{
                            background: 'none', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center',
                            width: '40px', height: '40px', borderRadius: '50%', cursor: 'pointer',
                            color: 'var(--color-text-muted)'
                        }}
                        onMouseOver={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                        onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                        title="Fechar detalhe"
                    >
                        <ArrowLeft size={20} />
                    </button>
                    {(totalCount !== undefined && totalCount > 1) && (
                        <div style={{
                            display: 'flex', alignItems: 'center', gap: '4px',
                            fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-muted)',
                            backgroundColor: 'var(--color-bg-page)', borderRadius: '9999px', padding: '4px 12px'
                        }}>
                            <button 
                                onClick={onPrev} disabled={currentIndex === 0} 
                                style={{
                                    background: 'none', border: 'none', cursor: currentIndex === 0 ? 'not-allowed' : 'pointer',
                                    opacity: currentIndex === 0 ? 0.3 : 1, color: 'inherit', fontWeight: 'bold'
                                }}
                            >
                                {'<'} 
                            </button>
                            <span style={{ margin: '0 8px' }}>{currentIndex !== undefined ? currentIndex + 1 : 1} / {totalCount}</span>
                            <button 
                                onClick={onNext} disabled={currentIndex === totalCount - 1} 
                                style={{
                                    background: 'none', border: 'none', cursor: currentIndex === totalCount - 1 ? 'not-allowed' : 'pointer',
                                    opacity: currentIndex === totalCount - 1 ? 0.3 : 1, color: 'inherit', fontWeight: 'bold'
                                }}
                            >
                                {'>'}
                            </button>
                        </div>
                    )}
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                    <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                        {requestNumber} - {currencyCode} {formatCurrencyAO(totalAmount)}
                    </span>
                </div>
                <button 
                    onClick={onOpenRequest}
                    style={{
                        background: 'none', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center',
                        width: '40px', height: '40px', borderRadius: '50%', cursor: 'pointer',
                        color: 'var(--color-text-muted)'
                    }}
                    onMouseOver={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                    onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                    title="Open Full Request"
                >
                    <MoreVertical size={20} />
                </button>
            </div>

            {/* Central Hero Identity */}
            <div style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                marginTop: '16px', gap: '12px'
            }}>
                <StatusBadge code={statusCode} name={statusName} color={statusBadgeColor} />
                <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                    {requestTypeCode}
                </span>
                <h1 style={{
                    fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', 
                    letterSpacing: '-0.02em', margin: 0, lineHeight: 1.1
                }}>
                    {currencyCode} {formatCurrencyAO(totalAmount)}
                </h1>
            </div>
        </div>
    );
}
