import { motion } from 'framer-motion';
import { WORKFLOW_STAGES } from './workflowData';
import { ChevronRight, RotateCcw } from 'lucide-react';

interface WorkflowInteractiveProps {
    selectedStageId: string;
    onSelectStage: (id: string) => void;
}

export function WorkflowInteractive({ selectedStageId, onSelectStage }: WorkflowInteractiveProps) {
    // We treat 'reajuste' as a special virtual state for the guide
    const isReajusteSelected = selectedStageId === 'reajuste';

    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '2rem',
            padding: '2rem',
            backgroundColor: '#f8fafc',
            border: '2px dashed var(--color-primary-light)',
            borderRadius: '0',
            marginBottom: '1.5rem',
            position: 'relative'
        }}>
            {/* Legend/Info label for educational context */}
            <div style={{
                position: 'absolute',
                top: '-12px',
                left: '20px',
                backgroundColor: 'var(--color-primary)',
                color: 'white',
                fontSize: '0.64rem',
                fontWeight: 800,
                padding: '3px 10px',
                textTransform: 'uppercase',
                zIndex: 1,
                letterSpacing: '0.05em'
            }}>
                Guia Visual de Processo
            </div>

            {/* Main Path Group */}
            <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: '0.5rem',
                overflowX: 'auto',
                paddingBottom: '0.5rem'
            }}>
                {WORKFLOW_STAGES.map((stage, index) => (
                    <div key={stage.id} style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flex: index === WORKFLOW_STAGES.length - 1 ? '0 1 auto' : 1 }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', alignItems: 'center', flex: 1 }}>
                            <motion.button
                                whileHover={{ scale: 1.02, backgroundColor: selectedStageId === stage.id ? 'var(--color-primary)' : '#f1f5f9' }}
                                whileTap={{ scale: 0.98 }}
                                onClick={() => onSelectStage(stage.id)}
                                style={{
                                    width: '100%',
                                    minWidth: '140px',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: 'center',
                                    gap: '0.25rem',
                                    padding: '0.85rem 0.5rem',
                                    backgroundColor: selectedStageId === stage.id ? 'var(--color-primary)' : 'white',
                                    border: `2px solid ${selectedStageId === stage.id ? 'var(--color-primary)' : 'var(--color-primary-light)'}`,
                                    boxShadow: selectedStageId === stage.id ? '4px 4px 0px rgba(var(--color-primary-rgb), 0.2)' : 'none',
                                    color: selectedStageId === stage.id ? 'white' : 'var(--color-primary)',
                                    cursor: 'pointer',
                                    transition: 'all 0.2s ease',
                                    position: 'relative',
                                    zIndex: 2
                                }}
                            >
                                <div style={{ 
                                    fontSize: '0.85rem', 
                                    fontWeight: 800,
                                    lineHeight: 1.2
                                }}>
                                    {stage.label}
                                </div>
                                <div style={{ 
                                    fontSize: '0.65rem', 
                                    fontWeight: 700, 
                                    textTransform: 'uppercase',
                                    opacity: selectedStageId === stage.id ? 0.9 : 0.75,
                                    letterSpacing: '0.02em'
                                }}>
                                    {stage.role}
                                </div>
                            </motion.button>
                        </div>

                        {index < WORKFLOW_STAGES.length - 1 && (
                            <div style={{ color: 'var(--color-primary-light)', flexShrink: 0, padding: '0 4px', opacity: 0.8 }}>
                                <ChevronRight size={18} strokeWidth={2} />
                            </div>
                        )}
                    </div>
                ))}
            </div>

            {/* Exceptions / Return Path Zone */}
            <div style={{
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                gap: '1rem',
                borderTop: '1px solid #e2e8f0',
                paddingTop: '1.5rem',
                position: 'relative'
            }}>
                {/* Visual connectors to reajuste concept */}
                <div style={{
                    position: 'absolute',
                    top: '-1px',
                    left: '50%',
                    transform: 'translateX(-50%)',
                    width: '60%',
                    height: '20px',
                    borderLeft: '2px dashed var(--color-primary-light)',
                    borderRight: '2px dashed var(--color-primary-light)',
                    borderBottom: '2px dashed var(--color-primary-light)',
                    opacity: 0.4,
                    pointerEvents: 'none'
                }} />

                <motion.button
                    whileHover={{ scale: 1.02 }}
                    whileTap={{ scale: 0.98 }}
                    onClick={() => onSelectStage('reajuste')}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.75rem',
                        padding: '0.6rem 1.2rem',
                        backgroundColor: isReajusteSelected ? '#fff7ed' : 'white',
                        border: `2px dashed ${isReajusteSelected ? '#f97316' : '#94a3b8'}`,
                        borderRadius: '2rem',
                        color: isReajusteSelected ? '#c2410c' : '#64748b',
                        cursor: 'pointer',
                        fontSize: '0.85rem',
                        fontWeight: 700,
                        transition: 'all 0.2s ease',
                        boxShadow: isReajusteSelected ? '0 4px 12px rgba(249, 115, 22, 0.1)' : 'none',
                        zIndex: 2
                    }}
                >
                    <RotateCcw size={16} />
                    <span>Conceito de Reajuste / Retorno</span>
                </motion.button>
                
                <div style={{ 
                    fontSize: '0.8rem', 
                    color: '#64748b', 
                    fontWeight: 500,
                    maxWidth: '320px',
                    lineHeight: 1.4
                }}>
                    * Etapa condicional onde o aprovador solicita correções ao comprador antes de seguir.
                </div>
            </div>
        </div>
    );
}
