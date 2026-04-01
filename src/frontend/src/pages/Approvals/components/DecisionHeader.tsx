import { 
    ShieldCheck, 
    ShieldAlert, 
    ExternalLink, 
    X, 
    ArrowRightLeft,
    AlertTriangle
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
    showAdjustmentAction
}: DecisionHeaderProps) {
    const isArea = approvalStage === 'AREA';

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
                backgroundColor: 'black',
                color: 'white',
                padding: '10px 24px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                     <span style={{ 
                        fontSize: '10px', 
                        fontWeight: 900, 
                        color: isArea ? 'var(--color-primary-light)' : '#10b981',
                        letterSpacing: '0.05em',
                        textTransform: 'uppercase',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px'
                     }}>
                        {isArea ? <ShieldAlert size={12} /> : <ShieldCheck size={12} />}
                        {isArea ? 'Fase de Aprovação da Área' : 'Fase de Aprovação Final'}
                     </span>
                     <div style={{ width: '1px', height: '12px', backgroundColor: 'rgba(255,255,255,0.2)' }} />
                     <span style={{ fontSize: '11px', fontWeight: 700, opacity: 0.8 }}>
                        {requestTypeCode}
                     </span>
                </div>
                
                <button 
                    onClick={onClose} 
                    style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'rgba(255,255,255,0.6)', padding: '2px', display: 'flex' }}
                    title="Fechar detalhe"
                >
                    <X size={20} />
                </button>
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
