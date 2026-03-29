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
            whileHover={onClick ? { y: -2, boxShadow: `6px 6px 0px ${color}`, transition: { duration: 0.1 } } : {}}
            onClick={onClick}
            style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: `2px solid ${color}`,
                boxShadow: `4px 4px 0px ${color}`,
                padding: '1.5rem',
                display: 'flex',
                flexDirection: 'column',
                gap: '0.5rem',
                position: 'relative',
                overflow: 'hidden',
                cursor: onClick ? 'pointer' : 'default'
            }}
        >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <span style={{ 
                    fontSize: '0.75rem', 
                    fontWeight: 700, 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    color: 'var(--color-text-muted)'
                }}>
                    {label}
                </span>
                {icon && <div style={{ color: color }}>{icon}</div>}
            </div>

            <div style={{ 
                fontSize: '2rem', 
                fontWeight: 800, 
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
            
            {/* Decorative corner accent */}
            <div style={{ 
                position: 'absolute', 
                bottom: 0, 
                right: 0, 
                width: '12px', 
                height: '12px', 
                backgroundColor: color,
                clipPath: 'polygon(100% 0, 0 100%, 100% 100%)'
            }} />
        </motion.div>
    );
}
