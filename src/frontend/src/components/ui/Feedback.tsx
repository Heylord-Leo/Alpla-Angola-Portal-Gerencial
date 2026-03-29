import React, { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { X, AlertCircle, CheckCircle, Info, AlertTriangle } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

export type FeedbackType = 'success' | 'error' | 'warning' | 'info';

interface FeedbackProps {
  type: FeedbackType;
  message: string | null;
  autoCloseMs?: number;
  onClose?: () => void;
  isFixed?: boolean;
}

const DEFAULT_TIMEOUTS: Record<FeedbackType, number> = {
  success: 5000,
  info: 5000,
  warning: 8000,
  error: 8000,
};

const TYPE_STYLES: Record<FeedbackType, { 
    bg: string, 
    border: string, 
    text: string, 
    iconColor: string, 
    Icon: React.ElementType 
}> = {
  success: {
    bg: '#F0FDF4',
    border: '#22C55E',
    text: '#15803D',
    iconColor: '#22C55E',
    Icon: CheckCircle,
  },
  error: {
    bg: '#FEF2F2',
    border: '#EF4444',
    text: '#B91C1C',
    iconColor: '#EF4444',
    Icon: AlertCircle,
  },
  warning: {
    bg: '#FFFBEB',
    border: '#F59E0B',
    text: '#B45309',
    iconColor: '#F59E0B',
    Icon: AlertTriangle,
  },
  info: {
    bg: '#EFF6FF',
    border: '#3B82F6',
    text: '#1D4ED8',
    iconColor: '#3B82F6',
    Icon: Info,
  },
};

export function Feedback({ type, message, autoCloseMs, onClose, isFixed = false }: FeedbackProps) {
  useEffect(() => {
    if (!message || !onClose) return;

    const timeout = autoCloseMs ?? DEFAULT_TIMEOUTS[type];
    const timer = setTimeout(() => {
      onClose();
    }, timeout);

    return () => clearTimeout(timer);
  }, [message, type, autoCloseMs, onClose]);

  if (!message) return null;

  const style = TYPE_STYLES[type];
  const { Icon } = style;

  const content = (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0, y: -10 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: -10 }}
        role="alert"
        style={{
          padding: '16px',
          backgroundColor: style.bg,
          color: style.text,
          borderRadius: 'var(--radius-sm)',
          borderLeft: `4px solid ${style.border}`,
          boxShadow: 'var(--shadow-brutal)',
          display: 'flex',
          alignItems: 'flex-start',
          gap: '12px',
          width: isFixed ? '400px' : '100%',
          position: isFixed ? 'fixed' : 'relative',
          top: isFixed ? '106px' : undefined,
          right: isFixed ? '24px' : undefined,
          zIndex: isFixed ? 9999 : undefined,
          marginBottom: isFixed ? 0 : '16px',
          pointerEvents: 'auto'
        }}
      >
        <Icon size={20} style={{ flexShrink: 0, marginTop: '2px', color: style.iconColor }} />
        <div style={{ flex: 1, fontWeight: 500, fontSize: '0.875rem', lineHeight: '1.5' }}>
          {message}
        </div>
        {onClose && (
          <button
            onClick={onClose}
            aria-label="Close message"
            style={{
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              color: style.text,
              padding: '2px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              opacity: 0.7,
              transition: 'opacity 0.2s'
            }}
            onMouseOver={(e) => (e.currentTarget.style.opacity = '1')}
            onMouseOut={(e) => (e.currentTarget.style.opacity = '0.7')}
          >
            <X size={18} />
          </button>
        )}
      </motion.div>
    </AnimatePresence>
  );

  if (isFixed) {
    return createPortal(
      <div style={{
        position: 'fixed',
        inset: 0,
        pointerEvents: 'none',
        zIndex: 9999,
        padding: '24px'
      }}>
        {content}
      </div>,
      document.body
    );
  }

  return content;
}
