import { motion } from 'framer-motion';

interface OperationalCardProps {
    label: string;
    value: number | string;
    helperText?: string;
    icon?: React.ReactNode;
    color?: string;
    isLoading?: boolean;
    onClick?: () => void;
}

export function OperationalCard({ label, value, helperText, icon, color = 'var(--color-primary)', isLoading, onClick }: OperationalCardProps) {
    return (
        <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            whileHover={onClick ? { y: -2, boxShadow: 'var(--shadow-md)', transition: { duration: 0.1 } } : {}}
            onClick={onClick}
            style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)',
                borderRadius: 'var(--radius-lg)',
                padding: '1.5rem',
                display: 'flex',
                flexDirection: 'column',
                gap: '0.75rem',
                position: 'relative',
                overflow: 'hidden',
                cursor: onClick ? 'pointer' : 'default',
                transition: 'border-color 0.2s'
            }}
        >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <span style={{ 
                    fontSize: '0.7rem', 
                    fontWeight: 700, 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.08em',
                    color: 'var(--color-text-muted)',
                    opacity: 0.8
                }}>
                    {label}
                </span>
                {icon && <div style={{ color: color }}>{icon}</div>}
            </div>

            <div style={{ 
                fontSize: '2rem', 
                fontWeight: 900, 
                fontFamily: 'var(--font-family-display)',
                color: 'var(--color-text-main)',
                lineHeight: 1
            }}>
                {isLoading ? '...' : value}
            </div>

            {helperText && (
                <div style={{ 
                    fontSize: '0.7rem', 
                    color: 'var(--color-text-muted)',
                    fontWeight: 500,
                    marginTop: '4px'
                }}>
                    {helperText}
                </div>
            )}
            
            {/* Subtle left-border emphasis instead of clip-path corner */}
            <div style={{ 
                position: 'absolute', 
                left: 0, 
                top: '25%', 
                bottom: '25%', 
                width: '4px', 
                backgroundColor: color,
                borderRadius: '0 4px 4px 0'
            }} />
        </motion.div>
    );
}
