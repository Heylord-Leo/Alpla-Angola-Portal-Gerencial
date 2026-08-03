import React, { useEffect, useRef, useState } from 'react';
import { Info } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { ModernTooltip } from '../ui/ModernTooltip';
import {
    DocumentUsageContext,
    documentTypeExplanations,
    documentTypeFieldPurpose
} from '../../lib/sourceDocumentType';

/**
 * The full explanation of "Tipo de documento anexado", on demand.
 *
 * <p>It exists because the explanation had been living permanently under the field: five lines of
 * consequence text that grew and shrank as the user changed the selection, pushing the rest of the
 * form around. The content was right; the place was wrong. Here it is one click away and costs the
 * form no vertical space at all.</p>
 *
 * <p>Each option is described in two parts — what the document <em>is</em>, then what the Portal
 * will <em>require</em> because of it. That separation is the whole point of the corrected taxonomy:
 * a Factura de Adiantamento is a fiscal document that still owes an operation invoice, and a
 * one-sentence description cannot say both.</p>
 */
export function DocumentTypeInfoModal({
    context,
    isOpen,
    onClose
}: {
    context: DocumentUsageContext;
    isOpen: boolean;
    onClose: () => void;
}) {
    // ModalWrapper closes on backdrop click but not on Escape.
    useEffect(() => {
        if (!isOpen) return;
        const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
        document.addEventListener('keydown', onKeyDown);
        return () => document.removeEventListener('keydown', onKeyDown);
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    return (
        <ModalWrapper title="Tipo de documento anexado" onClose={onClose} width={620}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <p style={{
                    margin: 0, fontSize: '0.8125rem', lineHeight: 1.55,
                    color: 'var(--color-text-muted)'
                }}>
                    {documentTypeFieldPurpose(context)}
                </p>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    {documentTypeExplanations(context).map(item => (
                        <div
                            key={item.value}
                            style={{
                                padding: '10px 12px',
                                borderRadius: 'var(--radius-sm, 8px)',
                                border: '1px solid var(--color-border)',
                                backgroundColor: 'var(--color-bg-page)'
                            }}
                        >
                            <div style={{
                                display: 'flex', alignItems: 'center', gap: '8px',
                                flexWrap: 'wrap', marginBottom: '4px'
                            }}>
                                <span style={{
                                    fontSize: '0.85rem', fontWeight: 700,
                                    color: 'var(--color-text-main)'
                                }}>
                                    {item.label}
                                </span>
                                <span style={{
                                    fontSize: '0.65rem', fontWeight: 700, padding: '2px 8px',
                                    borderRadius: '999px', letterSpacing: '0.02em',
                                    backgroundColor: item.isFiscal ? '#dcfce7' : '#f1f5f9',
                                    color: item.isFiscal ? '#15803d' : '#475569',
                                    border: `1px solid ${item.isFiscal ? '#86efac' : '#cbd5e1'}`
                                }}>
                                    {item.isFiscal ? 'Documento fiscal' : 'Não fiscal'}
                                </span>
                            </div>
                            <div style={{
                                fontSize: '0.78rem', lineHeight: 1.5,
                                color: 'var(--color-text-main)'
                            }}>
                                {item.whatItIs}
                            </div>
                            <div style={{
                                fontSize: '0.78rem', lineHeight: 1.5, marginTop: '2px',
                                color: 'var(--color-text-muted)'
                            }}>
                                {item.whatComesNext}
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        </ModalWrapper>
    );
}

/**
 * The info icon beside the field label: a short tooltip on hover, the full explanation on click.
 *
 * <p>A native <code>&lt;button&gt;</code> so Enter and Space work without re-implementing them.
 * The click is deliberately default-prevented: the field renders inside a <code>&lt;label&gt;</code>
 * on the payment screen, and without this the click would also be forwarded to the select.</p>
 */
export function DocumentTypeInfoTrigger({ context }: { context: DocumentUsageContext }) {
    const [isOpen, setIsOpen] = useState(false);
    const triggerRef = useRef<HTMLButtonElement>(null);

    const close = () => {
        setIsOpen(false);
        triggerRef.current?.focus();
    };

    return (
        <>
            <ModernTooltip
                side="top"
                maxWidth={260}
                triggerTabIndex={-1}
                content="Clique para ver a explicação dos tipos de documento"
            >
                <button
                    ref={triggerRef}
                    type="button"
                    onClick={e => { e.preventDefault(); e.stopPropagation(); setIsOpen(true); }}
                    aria-haspopup="dialog"
                    aria-label="Ver a explicação dos tipos de documento"
                    style={{
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                        padding: 0, background: 'none', border: 'none', cursor: 'pointer',
                        color: 'var(--color-primary)', lineHeight: 0
                    }}
                >
                    <Info size={14} />
                </button>
            </ModernTooltip>

            <DocumentTypeInfoModal context={context} isOpen={isOpen} onClose={close} />
        </>
    );
}
