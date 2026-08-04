import React, { useEffect, useRef } from 'react';
import { motion } from 'framer-motion';
import { AlertTriangle, Edit2, FileText, RefreshCw, Trash2, UploadCloud } from 'lucide-react';

/**
 * What the active document shows <b>instead of</b> the editor while its file is being read, and
 * when that reading fails.
 *
 * <p>The editor must not appear first. An empty form with an empty item table and an enabled
 * "Confirmar" button, next to a spinner small enough to miss, reads as <i>nothing happened — start
 * typing</i>. It also lets the user enter values that the extraction is about to overwrite.</p>
 *
 * <p>So extraction is a state of its own: the document area is one blocking view until the reading
 * finishes, and the editor is rendered for the first time already populated. There is no moment at
 * which an empty editor is visible.</p>
 */

interface LoadingProps {
    /** Shown so the user can see which document is being read. */
    fileName: string | null;
    sequence: number;
}

export function PaymentDocumentExtractionLoading({ fileName, sequence }: LoadingProps) {
    return (
        <section
            data-document-extracting={sequence}
            aria-busy="true"
            aria-live="polite"
            role="status"
            style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                gap: '14px', padding: '48px 24px', textAlign: 'center',
                border: '1px solid var(--color-border)',
                borderLeft: '3px solid var(--color-primary)',
                borderRadius: 'var(--radius-sm, 8px)',
                backgroundColor: 'var(--color-bg-surface)'
            }}
        >
            <motion.div
                animate={{ rotate: [0, 360] }}
                transition={{ repeat: Infinity, ease: 'linear', duration: 1.5 }}
                style={{ display: 'flex' }}
            >
                <RefreshCw size={44} style={{ color: 'var(--color-primary)' }} />
            </motion.div>

            <h3 style={{
                margin: 0, fontSize: '0.95rem', fontWeight: 900, letterSpacing: '0.05em',
                textTransform: 'uppercase', color: 'var(--color-primary)'
            }}>
                A analisar o Documento {sequence}
            </h3>

            <p style={{
                margin: 0, maxWidth: '440px', fontSize: '0.82rem', lineHeight: 1.55,
                color: 'var(--color-text-main)'
            }}>
                Estamos a extrair os dados da factura. Este processo pode demorar alguns segundos —
                o documento abre automaticamente quando estiver pronto.
            </p>

            {/* Indeterminate: the provider gives no progress, and a fake percentage would be a lie. */}
            <div style={{
                width: '160px', height: '4px', borderRadius: '2px', overflow: 'hidden',
                backgroundColor: 'var(--color-border)'
            }}>
                <motion.div
                    animate={{ x: ['-100%', '200%'] }}
                    transition={{ repeat: Infinity, ease: 'easeInOut', duration: 1.5 }}
                    style={{ width: '50%', height: '100%', backgroundColor: 'var(--color-primary)' }}
                />
            </div>

            {fileName && (
                <span style={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px', maxWidth: '100%',
                    fontSize: '0.78rem', fontWeight: 600, color: 'var(--color-text-muted)',
                    overflowWrap: 'anywhere'
                }}>
                    <FileText size={13} /> {fileName}
                </span>
            )}
        </section>
    );
}

interface ErrorProps {
    fileName: string | null;
    sequence: number;
    message: string;
    onRetry: () => void;
    /** Abandons the reading and opens the editor as a document the user fills in themselves. */
    onEnterManually: () => void;
    onChooseAnotherFile: () => void;
    onRemove: () => void;
}

/**
 * A reading that failed.
 *
 * <p>The editor is <b>not</b> opened automatically. A document created by "importar com OCR" whose
 * reading failed is not the same thing as a document the user chose to type — presenting one as the
 * other loses the distinction between "the extraction found nothing" and "there was nothing to
 * find". The user says which it is.</p>
 */
export function PaymentDocumentExtractionError({
    fileName, sequence, message, onRetry, onEnterManually, onChooseAnotherFile, onRemove
}: ErrorProps) {
    return (
        <section
            aria-live="polite"
            role="status"
            style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                gap: '12px', padding: '36px 24px', textAlign: 'center',
                border: '1px solid var(--color-border)',
                borderLeft: '3px solid #b45309',
                borderRadius: 'var(--radius-sm, 8px)',
                backgroundColor: 'var(--color-bg-surface)'
            }}
        >
            <AlertTriangle size={40} style={{ color: '#b45309' }} />

            <h3 style={{
                margin: 0, fontSize: '0.9rem', fontWeight: 900, letterSpacing: '0.04em',
                textTransform: 'uppercase', color: '#b45309'
            }}>
                Documento {sequence} — leitura falhou
            </h3>

            <p style={{
                margin: 0, maxWidth: '460px', fontSize: '0.82rem', lineHeight: 1.55,
                color: 'var(--color-text-main)'
            }}>
                {message}
            </p>

            {fileName && (
                <span style={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px', maxWidth: '100%',
                    fontSize: '0.78rem', fontWeight: 600, color: 'var(--color-text-muted)',
                    overflowWrap: 'anywhere'
                }}>
                    <FileText size={13} /> {fileName}
                </span>
            )}

            <div style={{
                display: 'flex', gap: '10px', flexWrap: 'wrap', justifyContent: 'center',
                marginTop: '4px'
            }}>
                <button type="button" onClick={onRetry} style={primaryAction}>
                    <RefreshCw size={14} /> Tentar novamente
                </button>
                <button type="button" onClick={onEnterManually} style={secondaryAction}>
                    <Edit2 size={14} /> Preencher manualmente
                </button>
                <button type="button" onClick={onChooseAnotherFile} style={secondaryAction}>
                    <UploadCloud size={14} /> Escolher outro ficheiro
                </button>
                <button
                    type="button"
                    onClick={onRemove}
                    style={{ ...secondaryAction, color: '#b91c1c' }}
                >
                    <Trash2 size={14} /> Remover documento
                </button>
            </div>
        </section>
    );
}

/**
 * Moves focus to the review area exactly once per document.
 *
 * <p>Once, because a reading that resolves while the user has already started typing must not pull
 * the caret out of the field they are in.</p>
 */
export function useFocusOnce(key: string | null, active: boolean) {
    const focused = useRef<Set<string>>(new Set());

    useEffect(() => {
        if (!key || !active || focused.current.has(key)) return;
        focused.current.add(key);

        requestAnimationFrame(() => {
            const card = window.document.querySelector<HTMLElement>(`[data-document-id="${key}"]`);
            card?.focus();
            card?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        });
    }, [key, active]);
}

const primaryAction: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '6px',
    padding: '9px 16px', borderRadius: '8px', border: 'none', cursor: 'pointer',
    backgroundColor: 'var(--color-primary)', color: '#fff', fontWeight: 800, fontSize: '0.8rem'
};

const secondaryAction: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '6px',
    padding: '9px 16px', borderRadius: '8px', cursor: 'pointer',
    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
    color: 'var(--color-text-main)', fontWeight: 700, fontSize: '0.8rem'
};
