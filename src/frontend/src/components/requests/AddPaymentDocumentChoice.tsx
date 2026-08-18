import React, { useEffect, useRef } from 'react';
import { Copy, Edit2, UploadCloud, X } from 'lucide-react';

export type AddDocumentMethod = 'OCR' | 'MANUAL' | 'DUPLICATE';

interface Props {
    /** The number the document being added will carry. */
    sequence: number;
    onChoose: (method: AddDocumentMethod) => void;
    /** Absent for the first document — there is nothing yet to duplicate, and nothing to cancel to. */
    onCancel?: () => void;
    /** Label of the document "Duplicar dados básicos" would copy from. */
    duplicateFrom?: number | null;
    disabled?: boolean;
    /**
     * <c>panel</c> is the first document: it <b>is</b> the screen, so it renders inline in the page.
     * <c>modal</c> is every document after it, which must not push the confirmed cards around.
     */
    variant: 'panel' | 'modal';
}

/**
 * How the next source document gets started.
 *
 * <p>The screen never opens an empty document card and asks the user to fill it in. It asks the one
 * question that actually comes first — <i>do you have a file, or are you typing this from
 * scratch?</i> — and only then creates the document. An empty card with a zero total, sitting next
 * to a form the user has already filled, is what made the previous screen read as "enter the same
 * invoice twice".</p>
 */
export function AddPaymentDocumentChoice({
    sequence,
    onChoose,
    onCancel,
    duplicateFrom,
    disabled = false,
    variant
}: Props) {
    const containerRef = useRef<HTMLDivElement>(null);
    const firstRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (variant !== 'modal') return;

        firstRef.current?.focus();

        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') { e.stopPropagation(); onCancel?.(); return; }
            if (e.key !== 'Tab') return;

            const focusable = containerRef.current?.querySelectorAll<HTMLElement>(
                'button:not([disabled])');
            if (!focusable || focusable.length === 0) return;

            const first = focusable[0];
            const last = focusable[focusable.length - 1];

            if (e.shiftKey && window.document.activeElement === first) {
                e.preventDefault(); last.focus();
            } else if (!e.shiftKey && window.document.activeElement === last) {
                e.preventDefault(); first.focus();
            }
        };

        window.document.addEventListener('keydown', onKey, true);
        return () => window.document.removeEventListener('keydown', onKey, true);
    }, [variant, onCancel]);

    const choices = (
        <div
            ref={containerRef}
            style={{
                display: 'grid',
                gridTemplateColumns: variant === 'panel'
                    ? 'repeat(auto-fit, minmax(280px, 1fr))'
                    : 'repeat(auto-fit, minmax(200px, 1fr))',
                gap: variant === 'panel' ? '16px' : '12px'
            }}
        >
            <Choice
                innerRef={firstRef}
                disabled={disabled}
                compact={variant === 'modal'}
                icon={<UploadCloud size={variant === 'panel' ? 32 : 24} style={{ color: '#2563eb' }} />}
                title="Importar com OCR"
                hint="Extrair os dados da fatura (PDF/imagem) automaticamente"
                border="#3b82f6"
                background="rgba(59, 130, 246, 0.08)"
                titleColor="#1e3a8a"
                onClick={() => onChoose('OCR')}
            />

            <Choice
                disabled={disabled}
                compact={variant === 'modal'}
                icon={<Edit2 size={variant === 'panel' ? 32 : 24} style={{ color: '#c026d3' }} />}
                title="Inserir manualmente"
                hint="Preencher os dados da fatura e anexar o ficheiro"
                border="#d946ef"
                background="rgba(217, 70, 239, 0.08)"
                titleColor="#701a75"
                onClick={() => onChoose('MANUAL')}
            />

            {duplicateFrom != null && (
                <Choice
                    disabled={disabled}
                    compact={variant === 'modal'}
                    icon={<Copy size={variant === 'panel' ? 32 : 24} style={{ color: '#0f766e' }} />}
                    title={`Duplicar dados do Documento ${duplicateFrom}`}
                    hint="Copia o fornecedor e a moeda. O ficheiro, o número, as datas, os valores e os itens são deste documento."
                    border="#14b8a6"
                    background="rgba(20, 184, 166, 0.08)"
                    titleColor="#115e59"
                    onClick={() => onChoose('DUPLICATE')}
                />
            )}
        </div>
    );

    if (variant === 'panel') {
        return (
            <section
                data-guide="request-add-first-document"
                style={{
                    padding: '20px', borderRadius: 'var(--radius-md, 10px)',
                    border: '1px dashed var(--color-border)',
                    backgroundColor: 'var(--color-bg-surface)'
                }}
            >
                <h3 style={{
                    margin: '0 0 4px', fontSize: '0.85rem', fontWeight: 900,
                    letterSpacing: '0.05em', textTransform: 'uppercase',
                    color: 'var(--color-text-main)'
                }}>
                    Adicionar primeiro documento
                </h3>
                <p style={{
                    margin: '0 0 16px', fontSize: '0.78rem', color: 'var(--color-text-muted)'
                }}>
                    Importe a fatura ou insira os dados manualmente. Poderá adicionar mais documentos
                    depois de confirmar este.
                </p>
                {choices}
            </section>
        );
    }

    return (
        <div
            role="dialog"
            aria-modal="true"
            aria-label={`Como deseja adicionar o Documento ${sequence}?`}
            onMouseDown={e => { if (e.target === e.currentTarget) onCancel?.(); }}
            style={{
                position: 'fixed', inset: 0, zIndex: 1200,
                display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px',
                backgroundColor: 'rgba(15, 23, 42, 0.45)', backdropFilter: 'blur(3px)'
            }}
        >
            <div style={{
                width: '100%', maxWidth: '640px', maxHeight: '90vh', overflowY: 'auto',
                padding: '20px', borderRadius: 'var(--radius-md, 10px)',
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-lg, 0 12px 32px rgba(0,0,0,0.25))'
            }}>
                <div style={{
                    display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between',
                    gap: '12px', marginBottom: '16px'
                }}>
                    <h3 style={{
                        margin: 0, fontSize: '0.85rem', fontWeight: 900, letterSpacing: '0.05em',
                        textTransform: 'uppercase', color: 'var(--color-text-main)'
                    }}>
                        Como deseja adicionar o Documento {sequence}?
                    </h3>
                    <button
                        type="button"
                        onClick={onCancel}
                        aria-label="Cancelar"
                        style={{
                            background: 'none', border: 'none', cursor: 'pointer', padding: '2px',
                            color: 'var(--color-text-muted)', lineHeight: 0
                        }}
                    >
                        <X size={18} />
                    </button>
                </div>

                {choices}

                <div style={{ marginTop: '16px', textAlign: 'right' }}>
                    <button
                        type="button"
                        onClick={onCancel}
                        style={{
                            padding: '8px 16px', borderRadius: '8px', cursor: 'pointer',
                            border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-page)',
                            color: 'var(--color-text-main)', fontWeight: 700, fontSize: '0.8rem'
                        }}
                    >
                        Cancelar
                    </button>
                </div>
            </div>
        </div>
    );
}

function Choice({
    innerRef, icon, title, hint, border, background, titleColor, onClick, disabled, compact
}: {
    innerRef?: React.Ref<HTMLButtonElement>;
    icon: React.ReactNode;
    title: string;
    hint: string;
    border: string;
    background: string;
    titleColor: string;
    onClick: () => void;
    disabled: boolean;
    compact: boolean;
}) {
    return (
        <button
            ref={innerRef}
            type="button"
            onClick={onClick}
            disabled={disabled}
            style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center',
                justifyContent: 'center', gap: compact ? '8px' : '12px', textAlign: 'center',
                padding: compact ? '18px 14px' : '32px 20px',
                border: `1px solid ${border}`, borderRadius: 'var(--radius-md, 10px)',
                backgroundColor: background,
                cursor: disabled ? 'not-allowed' : 'pointer',
                opacity: disabled ? 0.5 : 1
            }}
        >
            {icon}
            <span style={{
                fontSize: compact ? '0.8rem' : '0.9rem', fontWeight: 900,
                letterSpacing: '0.025em', textTransform: 'uppercase', color: titleColor
            }}>
                {title}
            </span>
            <span style={{
                fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 500,
                lineHeight: 1.4
            }}>
                {hint}
            </span>
        </button>
    );
}
