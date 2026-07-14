import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';

export function ModalWrapper({ 
    title, 
    onClose, 
    children, 
    wide, 
    width 
}: { 
    title: string; 
    onClose: () => void; 
    children: React.ReactNode; 
    wide?: boolean; 
    width?: number 
}) {
    const [mounted, setMounted] = useState(false);

    useEffect(() => {
        setMounted(true);
        return () => setMounted(false);
    }, []);

    if (!mounted) return null;

    const modalContent = (
        <>
            <div onClick={onClose} style={{
                position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', zIndex: 2000
            }} />
            <div style={{
                position: 'fixed', top: '50%', left: '50%', transform: 'translate(-50%, -50%)',
                width: width ?? (wide ? 640 : 480), maxHeight: '85vh', backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)', borderRadius: 14,
                boxShadow: '0 20px 60px rgba(0,0,0,0.2)', zIndex: 2001,
                display: 'flex', flexDirection: 'column',
                animation: 'modalIn 0.2s ease-out'
            }}>
                <style>{`@keyframes modalIn { from { opacity: 0; transform: translate(-50%, -48%); } to { opacity: 1; transform: translate(-50%, -50%); } }`}</style>
                <div style={{
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    padding: '14px 20px', borderBottom: '1px solid var(--color-border)'
                }}>
                    <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: 'var(--color-text)' }}>{title}</h3>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' }}>
                        <X size={18} />
                    </button>
                </div>
                <div style={{ padding: '16px 20px', overflowY: 'auto', flex: 1 }}>
                    {children}
                </div>
            </div>
        </>
    );

    return createPortal(modalContent, document.body);
}
