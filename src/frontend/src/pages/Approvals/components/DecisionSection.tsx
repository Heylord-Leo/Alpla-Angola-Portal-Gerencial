import { useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

interface DecisionSectionProps {
    title: string;
    icon?: React.ReactNode;
    count?: number;
    isCollapsible?: boolean;
    defaultOpen?: boolean;
    children: React.ReactNode;
    noPadding?: boolean;
}

export function DecisionSection({
    title,
    icon,
    count,
    isCollapsible = true,
    defaultOpen = true,
    children,
    noPadding = false
}: DecisionSectionProps) {
    const [isOpen, setIsOpen] = useState(defaultOpen);

    const toggle = () => {
        if (!isCollapsible) return;
        setIsOpen(!isOpen);
    };

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '2px solid black',
            boxShadow: 'var(--shadow-brutal)',
            marginBottom: '20px',
            overflow: 'hidden'
        }}>
            {/* --- Section Header --- */}
            <div 
                onClick={toggle}
                style={{
                    padding: '14px 20px',
                    backgroundColor: 'var(--color-bg-page)',
                    borderBottom: (isCollapsible && isOpen) || !isCollapsible ? '2px solid black' : 'none',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    cursor: isCollapsible ? 'pointer' : 'default',
                    userSelect: 'none'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {icon && <span style={{ color: 'var(--color-primary)', display: 'flex' }}>{icon}</span>}
                    <span style={{ 
                        fontWeight: 900, 
                        fontSize: '0.85rem', 
                        textTransform: 'uppercase', 
                        letterSpacing: '0.04em',
                        color: 'black'
                    }}>
                        {title}
                    </span>
                    {typeof count === 'number' && (
                        <span style={{ 
                            marginLeft: '4px', 
                            backgroundColor: 'black', 
                            color: 'white', 
                            padding: '2px 8px', 
                            fontSize: '0.7rem',
                            fontWeight: 800
                        }}>
                            {count}
                        </span>
                    )}
                </div>
                
                {isCollapsible && (
                    <motion.div
                        animate={{ rotate: isOpen ? 180 : 0 }}
                        transition={{ duration: 0.2 }}
                        style={{ display: 'flex', color: 'black' }}
                    >
                        <ChevronDown size={18} strokeWidth={2.5} />
                    </motion.div>
                )}
            </div>

            {/* --- Section Content --- */}
            <AnimatePresence initial={false}>
                {(!isCollapsible || isOpen) && (
                    <motion.div
                        initial={isCollapsible ? { height: 0, opacity: 0 } : false}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2, ease: 'easeInOut' }}
                    >
                        <div style={{ padding: noPadding ? 0 : '20px' }}>
                            {children}
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
