import { motion, AnimatePresence } from 'framer-motion';
import { X, BookOpen, Info, AlertTriangle, PlayCircle, CheckCircle2, FileText } from 'lucide-react';
import { DropdownPortal } from '../../../components/ui/DropdownPortal';
import { Z_INDEX } from '../../../constants/ui';

interface PurchasingHelpDrawerProps {
    isOpen: boolean;
    onClose: () => void;
}

export function PurchasingHelpDrawer({ isOpen, onClose }: PurchasingHelpDrawerProps) {
    
    // Content sections for the operational guide
    const sections = [
        {
            id: 'objetivo',
            title: 'Objetivo do Cockpit',
            icon: <Info size={18} />,
            content: 'Este painel centraliza a visão operacional de ponta a ponta da área de Compras e Logística. Ele foi projetado para destacar gargalos, monitorar o fluxo de pedidos e facilitar a tomada de decisão rápida.'
        },
        {
            id: 'kpis',
            title: 'Como ler os KPIs',
            icon: <FileText size={18} />,
            content: (
                <ul style={{ paddingLeft: '20px', margin: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <li><strong>Pedidos Abertos:</strong> Total de solicitações ativas no sistema.</li>
                    <li><strong>Em Cotação:</strong> Itens aguardando propostas de fornecedores.</li>
                    <li><strong>Aguardando Aprovação:</strong> Pedidos parados em alçada de gestão.</li>
                    <li><strong>Aguardando Pagamento:</strong> Processos financeiros em andamento.</li>
                    <li><strong>Recebimentos Pendentes:</strong> Materiais aguardando entrada física na planta.</li>
                </ul>
            )
        },
        {
            id: 'atencao',
            title: 'Pontos de Atenção',
            icon: <AlertTriangle size={18} />,
            content: 'Alertas críticos que exigem ação imediata. Itens aparecem aqui quando estão fora do prazo operacional ou parados em etapas críticas que impedem o fluxo da cadeia de suprimentos.'
        },
        {
            id: 'acoes',
            title: 'Ações Rápidas',
            icon: <PlayCircle size={18} />,
            content: 'Atalhos diretos para os fluxos mais comuns: criar novos pedidos, gerenciar cotações ativas ou processar recebimentos de materiais.'
        },
        {
            id: 'prioridade',
            title: 'Prioridade Recomendada',
            icon: <CheckCircle2 size={18} />,
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <p style={{ margin: 0 }}>Sugerimos seguir esta ordem para maximizar a eficiência operacional:</p>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {[
                            { step: 1, label: 'Tratar Pedidos Vencidos (Painel de Atenção)' },
                            { step: 2, label: 'Liberar Aprovações Pendentes' },
                            { step: 3, label: 'Acelerar Cotações em Atraso' },
                            { step: 4, label: 'Conciliar Recebimentos Pendentes' }
                        ].map(item => (
                            <div key={item.step} style={{ display: 'flex', alignItems: 'center', gap: '12px', fontSize: '0.85rem' }}>
                                <span style={{ 
                                    width: '24px', 
                                    height: '24px', 
                                    backgroundColor: 'var(--color-primary)', 
                                    color: 'white', 
                                    display: 'flex', 
                                    alignItems: 'center', 
                                    justifyContent: 'center', 
                                    fontWeight: 900,
                                    fontSize: '0.75rem'
                                }}>
                                    {item.step}
                                </span>
                                <span style={{ fontWeight: 700 }}>{item.label}</span>
                            </div>
                        ))}
                    </div>
                </div>
            )
        }
    ];

    return (
        <DropdownPortal>
            <AnimatePresence>
                {isOpen && (
                <>
                    {/* Backdrop */}
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        onClick={onClose}
                        style={{
                            position: 'fixed',
                            inset: 0,
                            backgroundColor: 'rgba(0, 0, 0, 0.4)',
                            backdropFilter: 'blur(4px)',
                            zIndex: Z_INDEX.DRAWER as any,
                            cursor: 'pointer'
                        }}
                    />

                    {/* Drawer */}
                    <motion.div
                        initial={{ x: '100%' }}
                        animate={{ x: 0 }}
                        exit={{ x: '100%' }}
                        transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                        style={{
                            position: 'fixed',
                            top: 0,
                            right: 0,
                            bottom: 0,
                            width: '100%',
                            maxWidth: '480px',
                            backgroundColor: 'var(--color-bg-page)',
                            borderLeft: '4px solid var(--color-primary)',
                            boxShadow: '-8px 0 32px rgba(0, 0, 0, 0.15)',
                            zIndex: `calc(${Z_INDEX.DRAWER} + 1)` as any,
                            display: 'flex',
                            flexDirection: 'column'
                        }}
                    >
                        {/* Header Area */}
                        <div style={{
                            padding: '32px',
                            backgroundColor: 'var(--color-primary)',
                            color: 'white',
                            position: 'relative'
                        }}>
                            <button
                                onClick={onClose}
                                style={{
                                    position: 'absolute',
                                    top: '20px',
                                    right: '20px',
                                    background: 'rgba(255, 255, 255, 0.1)',
                                    border: 'none',
                                    color: 'white',
                                    width: '32px',
                                    height: '32px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    cursor: 'pointer',
                                    transition: 'all 0.2s',
                                    borderRadius: '2px'
                                }}
                                onMouseOver={(e) => (e.currentTarget.style.backgroundColor = 'rgba(255,255,255,0.2)')}
                                onMouseOut={(e) => (e.currentTarget.style.backgroundColor = 'rgba(255, 255, 255, 0.1)')}
                            >
                                <X size={20} />
                            </button>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '20px' }}>
                                <div style={{
                                    backgroundColor: 'white',
                                    color: 'var(--color-primary)',
                                    padding: '12px',
                                    border: '3px solid var(--color-accent)',
                                    boxShadow: 'var(--shadow-md)'
                                }}>
                                    <BookOpen size={24} strokeWidth={2.5} />
                                </div>
                                <div>
                                    <h2 style={{
                                        margin: 0,
                                        fontSize: '1.4rem',
                                        fontWeight: 900,
                                        textTransform: 'uppercase',
                                        letterSpacing: '-0.01em',
                                        lineHeight: 1.1
                                    }}>
                                        Manual de Operação
                                    </h2>
                                    <p style={{ margin: '4px 0 0', fontSize: '0.75rem', fontWeight: 700, opacity: 0.8, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                        Guia Rápido • Compras & Logística
                                    </p>
                                </div>
                            </div>
                        </div>

                        {/* Content Area */}
                        <div style={{
                            flex: 1,
                            overflowY: 'auto',
                            padding: '32px',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '32px'
                        }}>
                            {sections.map(section => (
                                <section key={section.id}>
                                    <h3 style={{
                                        fontSize: '0.8rem',
                                        fontWeight: 900,
                                        color: 'var(--color-primary)',
                                        textTransform: 'uppercase',
                                        letterSpacing: '0.1em',
                                        marginBottom: '16px',
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: '10px',
                                        borderBottom: '1px solid var(--color-border)',
                                        paddingBottom: '8px'
                                    }}>
                                        {section.icon} {section.title}
                                    </h3>
                                    <div style={{ 
                                        fontSize: '0.9rem', 
                                        lineHeight: '1.6', 
                                        color: 'var(--color-text-main)',
                                        fontWeight: 500
                                    }}>
                                        {section.content}
                                    </div>
                                </section>
                            ))}

                            {/* Full Guide Placeholder */}
                            <section style={{ marginTop: 'auto', paddingTop: '32px' }}>
                                <div style={{
                                    padding: '24px',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '2px dashed var(--color-border)',
                                    textAlign: 'center'
                                }}>
                                    <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', margin: 0, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                        Guia Completo do Sistema
                                    </p>
                                    <div style={{
                                        marginTop: '12px',
                                        fontSize: '0.75rem',
                                        fontWeight: 800,
                                        color: 'var(--color-text-muted)',
                                        backgroundColor: 'var(--color-bg-page)',
                                        display: 'inline-block',
                                        padding: '4px 12px',
                                        border: '1px solid var(--color-border)',
                                        textTransform: 'uppercase'
                                    }}>
                                        Disponível em breve
                                    </div>
                                </div>
                            </section>
                        </div>

                        {/* Footer Area */}
                        <div style={{
                            padding: '24px 32px',
                            backgroundColor: 'white',
                            borderTop: '2px solid var(--color-border)',
                            display: 'flex',
                            justifyContent: 'flex-end'
                        }}>
                            <button 
                                onClick={onClose}
                                style={{
                                    padding: '12px 32px',
                                    backgroundColor: 'var(--color-primary)',
                                    color: 'white',
                                    border: 'none',
                                    fontWeight: 900,
                                    textTransform: 'uppercase',
                                    fontSize: '0.85rem',
                                    cursor: 'pointer',
                                    boxShadow: 'var(--shadow-md)',
                                    transition: 'all 0.1s'
                                }}
                                onMouseOver={(e) => (e.currentTarget.style.opacity = '0.88')}
                                onMouseOut={(e) => (e.currentTarget.style.opacity = '1')}
                            >
                                Entendido
                            </button>
                        </div>
                    </motion.div>
                </>
                )}
            </AnimatePresence>
        </DropdownPortal>
    );
}
