import { motion, AnimatePresence } from 'framer-motion';
import { WorkflowStage } from './workflowData';
import { Info, User, Target, Hash, ArrowRight, RotateCcw, AlertCircle } from 'lucide-react';

interface WorkflowStageDetailsProps {
    stage: WorkflowStage;
}

export function WorkflowStageDetails({ stage }: WorkflowStageDetailsProps) {
    const isReajuste = stage.id === 'reajuste';

    return (
        <AnimatePresence mode="wait">
            <motion.div
                key={stage.id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2 }}
                style={{
                    backgroundColor: 'white',
                    border: `2px solid ${isReajuste ? '#f97316' : 'var(--color-primary)'}`,
                    boxShadow: `8px 8px 0px ${isReajuste ? 'rgba(249, 115, 22, 0.1)' : 'rgba(var(--color-primary-rgb), 0.1)'}`,
                    padding: '2rem',
                    display: 'grid',
                    gridTemplateColumns: '1.2fr 0.8fr',
                    gap: '2.5rem',
                    position: 'relative'
                }}
            >
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
                    <header style={{ borderLeft: `4px solid ${isReajuste ? '#f97316' : 'var(--color-primary)'}`, paddingLeft: '1.25rem' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '0.25rem' }}>
                            <h3 style={{ margin: 0, fontSize: '1.6rem', fontWeight: 900, color: isReajuste ? '#c2410c' : 'var(--color-primary)', letterSpacing: '-0.02em' }}>
                                {stage.label}
                            </h3>
                            {isReajuste && (
                                <span style={{ 
                                    backgroundColor: '#fff7ed', 
                                    color: '#ea580c', 
                                    fontSize: '0.7rem', 
                                    fontWeight: 800, 
                                    padding: '2px 8px', 
                                    border: '1px solid #fdba74',
                                    textTransform: 'uppercase'
                                }}>
                                    Exceção / Retorno
                                </span>
                            )}
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'var(--color-text-muted)' }}>
                            <User size={14} strokeWidth={2.5} />
                            <span style={{ fontWeight: 700, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                {stage.responsible}
                            </span>
                        </div>
                    </header>

                    <section>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem', color: isReajuste ? '#c2410c' : 'var(--color-primary)' }}>
                            <Target size={18} strokeWidth={2.5} />
                            <h4 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Objetivo da Etapa</h4>
                        </div>
                        <p style={{ margin: 0, fontSize: '1rem', color: 'var(--color-text-main)', lineHeight: 1.6, fontWeight: 500 }}>
                            {stage.goal}
                        </p>
                    </section>

                    <section>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem', color: isReajuste ? '#c2410c' : 'var(--color-primary)' }}>
                            <Info size={18} strokeWidth={2.5} />
                            <h4 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Principais Atividades</h4>
                        </div>
                        <ul style={{ margin: 0, paddingLeft: '0', listStyle: 'none', display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
                            {stage.actions.map((action, i) => (
                                <li key={i} style={{ display: 'flex', gap: '0.75rem', fontSize: '0.95rem', color: 'var(--color-text-main)' }}>
                                    <span style={{ color: isReajuste ? '#f97316' : 'var(--color-primary)', fontWeight: 900 }}>•</span>
                                    <span>{action}</span>
                                </li>
                            ))}
                        </ul>
                    </section>
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '1.75rem' }}>
                    <section>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem', color: 'var(--color-text-muted)' }}>
                            <Hash size={16} strokeWidth={2.5} />
                            <h4 style={{ margin: 0, fontSize: '0.8rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.1em' }}>Documentação</h4>
                        </div>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem' }}>
                            {stage.documents.map((doc, i) => (
                                <span key={i} style={{ 
                                    backgroundColor: '#f1f5f9', 
                                    border: '1px solid #e2e8f0',
                                    padding: '5px 12px',
                                    fontSize: '0.75rem',
                                    fontWeight: 700,
                                    color: '#475569',
                                    borderRadius: '2rem'
                                }}>
                                    {doc}
                                </span>
                            ))}
                        </div>
                    </section>

                    <section style={{ 
                        marginTop: 'auto',
                        padding: '1.25rem', 
                        backgroundColor: isReajuste ? '#fff7ed' : '#f0f9ff', 
                        border: `2px solid ${isReajuste ? '#fed7aa' : '#bae6fd'}`,
                        position: 'relative',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '0.5rem'
                    }}>
                        <div style={{ 
                            display: 'flex', 
                            alignItems: 'center', 
                            gap: '0.5rem', 
                            color: isReajuste ? '#c2410c' : '#0369a1'
                        }}>
                            {isReajuste ? <RotateCcw size={18} strokeWidth={2.5} /> : <ArrowRight size={18} strokeWidth={2.5} />}
                            <h4 style={{ margin: 0, fontSize: '0.85rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                {isReajuste ? 'Fluxo de Retorno' : 'Próxima Etapa'}
                            </h4>
                        </div>
                        <p style={{ margin: 0, fontWeight: 800, fontSize: '1.1rem', color: isReajuste ? '#9a3412' : '#0c4a6e' }}>
                            {stage.nextStage}
                        </p>
                    </section>

                    {!isReajuste && stage.adjustmentPath && (
                        <section style={{ 
                            padding: '1rem', 
                            backgroundColor: '#fff7ed', 
                            border: '1px solid #fdba74',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '0.5rem'
                        }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', color: '#c2410c' }}>
                                <AlertCircle size={16} strokeWidth={2.5} />
                                <h4 style={{ margin: 0, fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Pode retornar para reajuste?</h4>
                            </div>
                            <p style={{ margin: 0, fontSize: '0.85rem', color: '#7c2d12', fontWeight: 600, lineHeight: 1.4 }}>
                                Sim. Se houver inconsistências, o aprovador solicitará correções (Setores: {stage.adjustmentPath.responsible}).
                            </p>
                        </section>
                    )}
                </div>
            </motion.div>
        </AnimatePresence>
    );
}
