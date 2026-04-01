import { 
    ShieldCheck, 
    ShieldAlert, 
    ExternalLink, 
    X, 
    ArrowRightLeft,
    AlertTriangle,
    ChevronLeft,
    ChevronRight
} from 'lucide-react';
import { StatusBadge } from '../../../components/ui/StatusBadge';
import { formatCurrencyAO } from '../../../lib/utils';
import { ApprovalActionType } from '../../../components/ApprovalModal';

interface DecisionHeaderProps {
    requestNumber: string;
    requestTypeCode: string;
    statusCode: string;
    statusName: string;
    statusBadgeColor: string;
    totalAmount: number;
    currencyCode: string;
    approvalStage: 'AREA' | 'FINAL';
    onAction: (action: ApprovalActionType) => void;
    onClose: () => void;
    onOpenRequest: () => void;
    isApproveBlocked: boolean;
    showAdjustmentAction: boolean;

    // Navigation props
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
    approvalStage,
    onAction,
    onClose,
    onOpenRequest,
    isApproveBlocked,
    showAdjustmentAction,
    onNext,
    onPrev,
    currentIndex,
    totalCount
}: DecisionHeaderProps) {
    const isArea = approvalStage === 'AREA';

    const hasNavigation = onNext && onPrev && totalCount !== undefined && totalCount > 1 && currentIndex !== undefined;

    return (
        <div style={{
            position: 'sticky',
            top: 0,
            zIndex: 100,
            backgroundColor: 'white',
            borderBottom: '4px solid black'
        }}>
            {/* --- Top Metadata Strip --- */}
            <div style={{
                backgroundColor: 'var(--color-bg-page)',
                color: 'black',
                padding: '12px 24px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                borderBottom: '1px solid var(--color-border)',
                borderLeft: `8px solid ${isArea ? 'var(--color-primary)' : '#10b981'}`
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                     <span style={{ 
                        fontSize: '11px', 
                        fontWeight: 900, 
                        color: isArea ? 'var(--color-primary)' : '#059669',
                        letterSpacing: '0.05em',
                        textTransform: 'uppercase',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px'
                     }}>
                        {isArea ? <ShieldAlert size={14} /> : <ShieldCheck size={14} />}
                        {isArea ? 'Fase de Aprovação da Área' : 'Fase de Aprovação Final'}
                     </span>
                     <div style={{ width: '1px', height: '14px', backgroundColor: 'var(--color-border)' }} />
                     <span style={{ fontSize: '11px', fontWeight: 800, color: 'var(--color-text-muted)' }}>
                        {requestTypeCode}
                     </span>
                 </div>
                
                <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                    {/* Navigation - improved visibility but clearly secondary */}
                    {hasNavigation && (
                         <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <button
                                onClick={(e) => { e.stopPropagation(); onPrev?.(); }}
                                disabled={currentIndex === 0}
                                style={{
                                    width: '32px',
                                    height: '32px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    border: '2px solid black',
                                    backgroundColor: 'white',
                                    color: 'black',
                                    opacity: currentIndex === 0 ? 0.2 : 1,
                                    cursor: currentIndex === 0 ? 'not-allowed' : 'pointer',
                                    boxShadow: currentIndex === 0 ? 'none' : '2px 2px 0px rgba(0,0,0,1)',
                                    transition: 'all 0.1s'
                                }}
                                title="Anterior (←)"
                            >
                                <ChevronLeft size={18} strokeWidth={2.5} />
                            </button>
                            
                            <span style={{ 
                                fontSize: '0.75rem', 
                                fontWeight: 900, 
                                color: 'black', 
                                minWidth: '50px', 
                                textAlign: 'center',
                                letterSpacing: '-0.02em'
                            }}>
                                {currentIndex + 1} <span style={{ opacity: 0.4, fontWeight: 500 }}>de</span> {totalCount}
                            </span>

                            <button
                                onClick={(e) => { e.stopPropagation(); onNext?.(); }}
                                disabled={currentIndex === (totalCount - 1)}
                                style={{
                                    width: '32px',
                                    height: '32px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    border: '2px solid black',
                                    backgroundColor: 'white',
                                    color: 'black',
                                    opacity: currentIndex === (totalCount - 1) ? 0.2 : 1,
                                    cursor: currentIndex === (totalCount - 1) ? 'not-allowed' : 'pointer',
                                    boxShadow: currentIndex === (totalCount - 1) ? 'none' : '2px 2px 0px rgba(0,0,0,1)',
                                    transition: 'all 0.1s'
                                }}
                                title="Próximo (→)"
                            >
                                <ChevronRight size={18} strokeWidth={2.5} />
                            </button>
                        </div>
                    )}

                    <div style={{ width: '2px', height: '24px', backgroundColor: 'var(--color-border)', margin: '0 4px' }} />

                    <button 
                        onClick={onClose} 
                        style={{ 
                            background: 'white', 
                            border: '2px solid black', 
                            cursor: 'pointer', 
                            color: 'black', 
                            padding: '4px', 
                            display: 'flex',
                            boxShadow: '2px 2px 0px rgba(0,0,0,1)',
                            transition: 'all 0.1s'
                        }}
                        title="Fechar detalhe"
                    >
                        <X size={20} strokeWidth={3} />
                    </button>
                </div>
            </div>

            {/* --- Main Identity & Status --- */}
            <div style={{
                padding: '20px 24px',
                display: 'flex',
                alignItems: 'flex-start',
                justifyContent: 'space-between',
                gap: '24px',
                flexWrap: 'wrap'
            }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <h1 style={{ 
                            fontSize: '1.75rem', 
                            fontWeight: 900, 
                            letterSpacing: '-0.04em',
                            margin: 0,
                            lineHeight: 1
                        }}>
                            {requestNumber}
                        </h1>
                        <StatusBadge code={statusCode} name={statusName} color={statusBadgeColor} />
                    </div>
                    
                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                        <button 
                            onClick={onOpenRequest}
                            style={{
                                display: 'flex', alignItems: 'center', gap: '6px',
                                background: 'none', border: 'none', color: 'var(--color-primary)',
                                fontWeight: 800, fontSize: '10px', textTransform: 'uppercase',
                                cursor: 'pointer', padding: 0
                            }}
                        >
                            <ExternalLink size={12} /> Abrir Pedido Completo
                        </button>
                    </div>
                </div>

                <div style={{ textAlign: 'right' }}>
                    <div style={{ fontSize: '10px', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '2px' }}>
                        Total do Pedido
                    </div>
                    <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'flex-end', gap: '6px' }}>
                        <span style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-text-muted)' }}>{currencyCode || 'AKZ'}</span>
                        <span style={{ fontSize: '1.8rem', fontWeight: 900, letterSpacing: '-0.03em' }}>{formatCurrencyAO(totalAmount)}</span>
                    </div>
                </div>
            </div>

            {/* --- Decision Action Row --- */}
            <div style={{
                padding: '16px 24px',
                backgroundColor: '#fafafa',
                borderTop: '2px solid black',
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                flexWrap: 'wrap'
            }}>
                <button
                    onClick={() => onAction('APPROVE')}
                    disabled={isApproveBlocked}
                    style={{
                        backgroundColor: isApproveBlocked ? '#e5e7eb' : '#10b981',
                        color: isApproveBlocked ? '#9ca3af' : 'white',
                        border: '2px solid black',
                        padding: '10px 24px',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        fontWeight: 900,
                        fontSize: '0.8rem',
                        textTransform: 'uppercase',
                        cursor: isApproveBlocked ? 'not-allowed' : 'pointer',
                        transition: 'all 0.1s',
                        boxShadow: isApproveBlocked ? 'none' : '4px 4px 0 black'
                    }}
                    title={isApproveBlocked ? 'Selecione uma cotação vencedora' : 'Aprovar Pedido'}
                >
                    <ShieldCheck size={18} /> APROVAR
                </button>

                {showAdjustmentAction && (
                    <button
                        onClick={() => onAction('REQUEST_ADJUSTMENT')}
                        style={{
                            backgroundColor: 'white',
                            color: 'black',
                            border: '2px solid black',
                            padding: '10px 24px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            fontWeight: 900,
                            fontSize: '0.8rem',
                            textTransform: 'uppercase',
                            cursor: 'pointer',
                            boxShadow: '4px 4px 0 black'
                        }}
                    >
                        <ArrowRightLeft size={18} /> REAJUSTE
                    </button>
                )}

                <button
                    onClick={() => onAction('REJECT')}
                    style={{
                        backgroundColor: 'white',
                        color: '#ef4444',
                        border: '2px solid #ef4444',
                        padding: '10px 24px',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        fontWeight: 900,
                        fontSize: '0.8rem',
                        textTransform: 'uppercase',
                        cursor: 'pointer',
                        boxShadow: '4px 4px 0 #ef4444'
                    }}
                >
                    <AlertTriangle size={18} /> REJEITAR
                </button>
            </div>
        </div>
    );
}
