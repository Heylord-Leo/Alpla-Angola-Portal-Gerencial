import React, { useMemo, useRef } from 'react';
import {
    AlertCircle, AlertTriangle, CheckCircle2, ChevronDown, ChevronRight,
    Copy, FileText, Loader2, Paperclip, Trash2, UserPlus
} from 'lucide-react';
import { PaymentSourceDocumentDto, PaymentDocumentOcrState } from '../../types/paymentSourceDocument';
import { OcrDocumentClassification, ClassificationConflictState } from '../../lib/documentClassificationDecision';
import { deriveCardStatus, describeDocument } from '../../lib/paymentSourceDocuments';
import { SourceDocumentTypeField } from './SourceDocumentTypeField';
import { FieldMessageIcon } from '../ui/FieldMessageIcon';
import { MoneyInput } from '../ui/MoneyInput';
import { SupplierAutocomplete } from '../SupplierAutocomplete';
import { DateInput } from '../DateInput';
import { formatCurrencyAO } from '../../lib/utils';

export interface PaymentSourceDocumentCardProps {
    document: PaymentSourceDocumentDto;
    ocr: PaymentDocumentOcrState;
    conflict: ClassificationConflictState;

    isExpanded: boolean;
    onToggle: () => void;

    /**
     * <c>editor</c> is the one document currently being worked on: it is always open and its header
     * cannot be collapsed, because collapsing the thing you are editing is how unsaved changes get
     * abandoned by accident. <c>collapsible</c> is the ordinary reviewable card.
     */
    variant?: 'collapsible' | 'editor';
    /** Actions that close out the document — "Confirmar e adicionar documento" and its siblings. */
    footer?: React.ReactNode;
    /**
     * Replaces the derived card status.
     *
     * <p>The composer owns the document's lifecycle while it is open, and its vocabulary is the
     * honest one for a document being worked on: <i>Em extração</i> while the file is being read,
     * <i>Em revisão</i> once it has been. A document nobody has had a chance to fill in yet must
     * never be labelled "Incompleto".</p>
     */
    statusOverride?: { label: string; severity: 'success' | 'warning' | 'error' | 'muted';
                       isExtracting?: boolean } | null;

    readOnly: boolean;
    /** Reported per card: a failure in one document must not discard another's unsaved state. */
    saveError: string | null;
    isSaving: boolean;

    /**
     * A patch of the document being edited.
     *
     * <p>Widened by one composition-only key: `supplierInternalCompany` is how the creation flow
     * records (and clears) the "this counterparty is ALPLA" verdict on its temporary document. It is
     * client state, never persisted — the persisted side reports the same fact through
     * `supplierIsInternalCompany` on the DTO.</p>
     */
    onFieldChange: (patch: Partial<PaymentSourceDocumentDto> & {
        supplierInternalCompany?: { id: number; name: string } | null;
    }) => void;
    onConflictChange: (next: ClassificationConflictState) => void;
    onReplaceAttachment: () => void;
    onRemove: () => void;
    onDuplicate: () => void;
    /** Hidden while a document is being composed — there is nothing to duplicate from yet. */
    showDuplicate?: boolean;

    /**
     * Opens the shared supplier quick-create for this document's unmatched supplier.
     *
     * <p>Absent when the card is read-only or the user may not create suppliers — the action is not
     * rendered rather than rendered-and-refused.</p>
     */
    onCreateSupplier?: ((name: string, taxId: string) => void) | null;
    /** Why creation is unavailable, when it is. Shown in place of the action. */
    supplierCreateDisabledReason?: string | null;
    /**
     * The user has tried to confirm or submit. Until then an unmatched OCR supplier is amber, not
     * red: the extraction read a real name off a real invoice, and calling that corrupt input
     * before the user has had any chance to act blames them for the Portal not knowing the supplier.
     */
    showValidationErrors?: boolean;

    plants: Array<{ id: number; name: string }>;
    currencies: Array<{ code: string; name: string }>;
    /** Blocks a second currency before the backend has to refuse it. */
    currencyLocked: string | null;
    currencyError: string | null;

    children?: React.ReactNode;
}

const SEVERITY_COLOR: Record<string, string> = {
    success: '#15803d',
    warning: '#b45309',
    error: '#b91c1c',
    muted: '#64748b'
};

/**
 * One source document of a PAYMENT request, as a collapsible card.
 *
 * <p><b>Everything about this document is local to this card.</b> Its OCR result, classification
 * suggestion, conflict answer, items, totals and concurrency token all arrive as props keyed by the
 * document's own id — nothing is read from a request-level variable. That is the property the whole
 * feature rests on: uploading Documento 2 must not touch Documento 1.</p>
 *
 * <p>Collapsed, the header identifies the document without opening it. A three-invoice request must
 * not become one enormous scrolling form, and the user must be able to see which card is the one
 * still missing something.</p>
 */
export function PaymentSourceDocumentCard({
    document,
    ocr,
    conflict,
    isExpanded,
    onToggle,
    variant = 'collapsible',
    footer,
    statusOverride = null,
    readOnly,
    saveError,
    isSaving,
    onFieldChange,
    onConflictChange,
    onReplaceAttachment,
    onRemove,
    onDuplicate,
    showDuplicate = true,
    onCreateSupplier = null,
    supplierCreateDisabledReason = null,
    showValidationErrors = false,
    plants,
    currencies,
    currencyLocked,
    currencyError,
    children
}: PaymentSourceDocumentCardProps) {
    const headerRef = useRef<HTMLButtonElement>(null);
    const status = useMemo(
        () => deriveCardStatus(document, ocr.isProcessing, ocr.classification),
        [document, ocr.isProcessing, ocr.classification]);

    const isEditor = variant === 'editor';
    const open = isEditor || isExpanded;
    /** A name was read (or typed) but does not correspond to a registered supplier. */
    /**
     * The counterparty read off this document is an ALPLA legal entity.
     *
     * <p>Kept apart from `supplierUnresolved` on purpose. "Not registered yet" is answered by
     * registering the supplier; this is answered by checking whether the right file was attached.
     * Offering "Criar fornecedor" here would invite the user to create a second Supplier row for
     * ALPLA itself — exactly what must not happen.</p>
     */
    const supplierIsInternal = !!document.supplierIsInternalCompany;

    const supplierUnresolved =
        !supplierIsInternal && !document.supplierId && !!document.supplierNameSnapshot;
    const bodyId = `psd-body-${document.id}`;
    const accent = SEVERITY_COLOR[statusOverride?.severity ?? status.severity];

    const labelStyle: React.CSSProperties = {
        display: 'block', fontSize: '0.72rem', fontWeight: 700,
        color: 'var(--color-text-muted)', marginBottom: '4px'
    };
    const inputStyle: React.CSSProperties = {
        width: '100%', boxSizing: 'border-box', padding: '8px 10px', fontSize: '0.85rem',
        borderRadius: 'var(--radius-sm, 8px)', border: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)'
    };

    const field = (label: string, node: React.ReactNode) => (
        <label style={labelStyle}>
            {label}
            {node}
        </label>
    );

    return (
        <div
            data-document-id={document.id}
            // Programmatically focusable so the review area can be reached once when a reading
            // lands, without becoming a tab stop of its own.
            tabIndex={isEditor ? -1 : undefined}
            aria-label={isEditor ? `Rever Documento ${document.sequenceNumber}` : undefined}
            style={{
                outline: 'none',
                border: `1px solid ${open ? accent : 'var(--color-border)'}`,
                borderLeft: `3px solid ${accent}`,
                borderRadius: 'var(--radius-sm, 8px)',
                backgroundColor: 'var(--color-bg-surface)',
                opacity: document.isVoided ? 0.6 : 1,
                overflow: 'hidden'
            }}
        >
            {/* Header. In editor mode it is a caption, not a control: the document being edited is
                not collapsible, because collapsing it is how unsaved work gets abandoned. */}
            {React.createElement(
                isEditor ? 'div' : 'button',
                isEditor
                    ? {
                        style: {
                            width: '100%', display: 'flex', alignItems: 'center', gap: '10px',
                            padding: '10px 12px', textAlign: 'left', color: 'var(--color-text-main)',
                            borderBottom: '1px solid var(--color-border)'
                        }
                    }
                    : {
                        ref: headerRef,
                        type: 'button',
                        onClick: onToggle,
                        'aria-expanded': isExpanded,
                        'aria-controls': bodyId,
                        style: {
                            width: '100%', display: 'flex', alignItems: 'center', gap: '10px',
                            padding: '10px 12px', background: 'none', border: 'none',
                            cursor: 'pointer', textAlign: 'left', color: 'var(--color-text-main)'
                        }
                    },
                <React.Fragment key="header">
                    {!isEditor && (isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />)}

                    <span style={{ fontWeight: 800, fontSize: '0.85rem', flexShrink: 0 }}>
                        Documento {document.sequenceNumber}
                    </span>

                    {/* Long supplier names must wrap, never push the total off the row. */}
                    <span style={{
                        flex: 1, minWidth: 0, fontSize: '0.8rem', color: 'var(--color-text-muted)',
                        overflowWrap: 'anywhere'
                    }}>
                        {describeDocument(document)}
                    </span>

                    <span style={{ fontWeight: 700, fontSize: '0.85rem', whiteSpace: 'nowrap' }}>
                        {formatCurrencyAO(document.grossAmount ?? 0)} {document.currency ?? ''}
                    </span>

                    <span style={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px', flexShrink: 0,
                        fontSize: '0.7rem', fontWeight: 700, color: accent, whiteSpace: 'nowrap'
                    }}>
                        {statusOverride ? (
                            <>
                                {statusOverride.isExtracting
                                    ? <Loader2 size={13} className="spin-icon" />
                                    : statusOverride.severity === 'success' ? <CheckCircle2 size={13} />
                                    : statusOverride.severity === 'error' ? <AlertCircle size={13} />
                                    : <AlertTriangle size={13} />}
                                {statusOverride.label}
                            </>
                        ) : (
                            <>
                                {status.status === 'OCR_PENDING' && <Loader2 size={13} className="spin-icon" />}
                                {status.status === 'READY' && <CheckCircle2 size={13} />}
                                {status.status === 'INCOMPLETE' && <AlertTriangle size={13} />}
                                {status.status === 'CLASSIFICATION_CONFLICT' && <AlertCircle size={13} />}
                                {status.label}
                            </>
                        )}
                    </span>
                </React.Fragment>
            )}

            {/* Persistent, not a toast: a failed save must stay on screen until it is dealt with. */}
            {saveError && (
                <div role="alert" style={{
                    margin: '0 12px 8px', padding: '8px 10px', borderRadius: '6px',
                    backgroundColor: 'rgba(185,28,28,0.08)', border: '1px solid #fca5a5',
                    color: '#b91c1c', fontSize: '0.75rem', fontWeight: 600
                }}>
                    {saveError}
                </div>
            )}

            {ocr.error && (
                <div role="alert" style={{
                    margin: '0 12px 8px', padding: '8px 10px', borderRadius: '6px',
                    backgroundColor: 'rgba(180,83,9,0.08)', border: '1px solid #fcd34d',
                    color: '#b45309', fontSize: '0.75rem', fontWeight: 600
                }}>
                    {ocr.error}
                </div>
            )}

            {open && (
                <div id={bodyId} style={{
                    padding: '12px', display: 'flex', flexDirection: 'column', gap: '12px'
                }}>
                    {/* ── Attachment ── */}
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap',
                        padding: '8px 10px', borderRadius: '6px',
                        backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)'
                    }}>
                        <Paperclip size={14} color="var(--color-text-muted)" />
                        <span style={{ flex: 1, minWidth: 0, fontSize: '0.8rem', overflowWrap: 'anywhere' }}>
                            {document.attachmentFileName ?? 'Sem anexo'}
                        </span>
                        {!readOnly && (
                            <button type="button" onClick={onReplaceAttachment} style={linkButton}>
                                Substituir anexo
                            </button>
                        )}
                    </div>

                    {/* ── Identity grid ── */}
                    <div style={{
                        display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                        gap: '10px'
                    }}>
                        {/* The same searchable field the single-document editor uses. A supplier
                            list cannot be preloaded here — there are thousands — and an extraction
                            that read a name the portal does not know must still be visible as
                            unresolved rather than silently blank. */}
                        <div style={{ gridColumn: '1 / -1' }} data-document-supplier>
                            <span style={labelStyle}>
                                Fornecedor
                                {supplierUnresolved && (
                                    <FieldMessageIcon
                                        severity="warning"
                                        tooltip="O fornecedor lido ainda não existe no Portal."
                                        title="Fornecedor não reconhecido"
                                        maxWidth={520}
                                    >
                                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55 }}>
                                            O documento indica <strong>{document.supplierNameSnapshot}</strong>,
                                            que ainda não corresponde a nenhum fornecedor registado no Portal.
                                        </p>
                                        <p style={{ margin: '10px 0 0', fontSize: '0.8125rem', lineHeight: 1.55 }}>
                                            Isto não significa que a leitura esteja errada — significa apenas que
                                            este fornecedor ainda não foi registado. Pode registá-lo a partir daqui
                                            ou selecionar o fornecedor correto. Um pagamento não pode ser dirigido a
                                            um fornecedor que o Portal não conhece, por isso o documento só fica
                                            completo depois de um deles estar escolhido.
                                        </p>
                                    </FieldMessageIcon>
                                )}
                            </span>
                            <SupplierAutocomplete
                                initialName={document.supplierNameSnapshot || ''}
                                isUnresolved={supplierUnresolved}
                                // Amber while it is merely unregistered; red only once the user has
                                // tried to move on without resolving it. An internal entity is red
                                // immediately — it is not a pending step, it is a wrong answer.
                                hasWarning={supplierUnresolved && !showValidationErrors}
                                hasError={supplierIsInternal || (supplierUnresolved && showValidationErrors)}
                                disabled={readOnly}
                                // Payable-supplier context: ALPLA legal entities are not offered.
                                excludeInternal
                                onChange={(id, name) => onFieldChange({
                                    supplierId: id,
                                    supplierNameSnapshot: name || null,
                                    // Choosing a real supplier answers the question the warning was
                                    // asking, so the warning goes with it.
                                    supplierInternalCompany: null
                                })}
                            />

                            {supplierIsInternal && (
                                <div
                                    role="alert"
                                    style={{
                                        marginTop: '8px', padding: '10px 12px', borderRadius: '6px',
                                        border: '1px solid #fca5a5', backgroundColor: 'rgba(185,28,28,0.06)',
                                        color: '#b91c1c', fontSize: '0.78rem', lineHeight: 1.55,
                                        display: 'flex', gap: '8px', alignItems: 'flex-start'
                                    }}
                                >
                                    <AlertTriangle size={15} style={{ flexShrink: 0, marginTop: '1px' }} />
                                    <span>
                                        <strong>
                                            A empresa identificada como emitente pertence à ALPLA e não
                                            pode ser utilizada como fornecedor em um pedido de pagamento.
                                        </strong>{' '}
                                        Verifique se o documento selecionado é o correto.
                                        <span style={{ display: 'block', marginTop: '6px', fontWeight: 500 }}>
                                            Este documento parece ter sido emitido por uma empresa ALPLA
                                            para um cliente externo. Um documento emitido pela ALPLA não
                                            origina um pagamento — se este for o ficheiro certo, o
                                            fornecedor a pagar é outra entidade.
                                        </span>
                                    </span>
                                </div>
                            )}

                            {supplierUnresolved && !readOnly && (
                                <div style={{
                                    display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap',
                                    marginTop: '6px', fontSize: '0.75rem', color: '#b45309', fontWeight: 600
                                }}>
                                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '5px' }}>
                                        <AlertTriangle size={13} />
                                        Não encontrado no Portal.
                                    </span>

                                    {onCreateSupplier ? (
                                        <button
                                            type="button"
                                            onClick={() => onCreateSupplier(
                                                document.supplierNameSnapshot ?? '',
                                                document.supplierTaxIdSnapshot ?? '')}
                                            style={{ ...linkButton, textDecoration: 'underline' }}
                                        >
                                            <UserPlus size={13} /> Criar fornecedor
                                        </button>
                                    ) : supplierCreateDisabledReason ? (
                                        <FieldMessageIcon
                                            severity="info"
                                            tooltip="Registo de fornecedores não disponível para o seu perfil."
                                            title="Registo de fornecedores"
                                            maxWidth={520}
                                        >
                                            <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55 }}>
                                                {supplierCreateDisabledReason}
                                            </p>
                                        </FieldMessageIcon>
                                    ) : null}

                                    {/* No "selecionar outro fornecedor" action: the field directly
                                        above is already the search, and a link that only moves the
                                        caret into it is a second answer to a question the user has
                                        not asked. */}
                                </div>
                            )}
                        </div>

                        {field('Planta', (
                            <select
                                data-field="plantId"
                                value={document.plantId ?? ''}
                                disabled={readOnly}
                                onChange={e => onFieldChange({
                                    plantId: e.target.value ? Number(e.target.value) : null
                                })}
                                style={inputStyle}
                            >
                                <option value="">-- Selecione --</option>
                                {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                            </select>
                        ))}

                        {field('Nº Documento', (
                            <input
                                data-field="documentNumber"
                                type="text"
                                value={document.documentNumber ?? ''}
                                disabled={readOnly}
                                onChange={e => onFieldChange({ documentNumber: e.target.value })}
                                style={inputStyle}
                            />
                        ))}

                        {field('Série', (
                            <input
                                type="text"
                                value={document.documentSeries ?? ''}
                                disabled={readOnly}
                                onChange={e => onFieldChange({ documentSeries: e.target.value })}
                                style={inputStyle}
                            />
                        ))}

                        {/* The Release 2 field, reused whole — classification logic exists once. */}
                        <SourceDocumentTypeField
                            context="PAYMENT_REQUEST"
                            value={document.sourceDocumentType ?? ''}
                            onChange={val => onFieldChange({ sourceDocumentType: val })}
                            ocr={ocr.classification}
                            conflict={conflict}
                            onConflictChange={onConflictChange}
                            readOnly={readOnly}
                            required
                            labelStyle={labelStyle}
                            inputStyle={inputStyle}
                        />

                        {/* DateInput, not <input type="date">. A native date input renders its value
                            and its placeholder according to the BROWSER's locale, so on an en-US
                            profile the correctly stored 2026-08-10 was displayed as 08/10/2026 with
                            an 'mm/dd/yyyy' placeholder — a date the user in Angola reads as 8
                            October. The value was never wrong; only its presentation was. DateInput
                            keeps the value ISO and displays dd/MM/yyyy regardless of locale. */}
                        {field('Data do documento', (
                            <DateInput
                                data-field="documentDate"
                                value={(document.documentDate ?? '').substring(0, 10)}
                                disabled={readOnly}
                                onChange={value => onFieldChange({ documentDate: value || null })}
                                style={inputStyle}
                            />
                        ))}

                        {field('Data de vencimento', (
                            <DateInput
                                data-field="dueDate"
                                value={(document.dueDate ?? '').substring(0, 10)}
                                disabled={readOnly}
                                onChange={value => onFieldChange({ dueDate: value || null })}
                                hasError={showValidationErrors && !document.dueDate}
                                style={inputStyle}
                            />
                        ))}

                        <label style={labelStyle}>
                            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                                Moeda
                                {currencyLocked && (
                                    <FieldMessageIcon
                                        severity="info"
                                        tooltip="Um pedido de pagamento suporta apenas uma moeda."
                                        title="Moeda do pedido"
                                        maxWidth={520}
                                    >
                                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55 }}>
                                            Este pedido já está em <strong>{currencyLocked}</strong>. Um pedido de
                                            pagamento suporta apenas uma moeda, para que o total consolidado
                                            signifique alguma coisa. Documentos noutra moeda têm de ser pagos num
                                            pedido separado — os valores nunca são convertidos automaticamente.
                                        </p>
                                    </FieldMessageIcon>
                                )}
                            </span>
                            <select
                                data-field="currency"
                                value={document.currency ?? ''}
                                disabled={readOnly}
                                onChange={e => onFieldChange({ currency: e.target.value || null })}
                                style={{
                                    ...inputStyle,
                                    borderColor: currencyError ? '#fca5a5' : 'var(--color-border)'
                                }}
                            >
                                <option value="">-- Selecione --</option>
                                {currencies.map(c => (
                                    <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
                                ))}
                            </select>
                            {currencyError && (
                                <span style={{
                                    display: 'block', color: '#b91c1c', fontSize: '0.72rem',
                                    marginTop: '4px', fontWeight: 600
                                }}>
                                    {currencyError}
                                </span>
                            )}
                        </label>
                    </div>

                    {/* ── Amounts ── */}
                    <div style={{
                        display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
                        gap: '10px'
                    }}>
                        {field('Valor líquido', (
                            <MoneyInput
                                value={document.netAmount ?? ''}
                                disabled={readOnly}
                                onChange={v => onFieldChange({
                                    netAmount: v === '' ? null : Number(v)
                                })}
                                style={inputStyle}
                            />
                        ))}
                        {field('IVA', (
                            <MoneyInput
                                value={document.taxAmount ?? ''}
                                disabled={readOnly}
                                onChange={v => onFieldChange({
                                    taxAmount: v === '' ? null : Number(v)
                                })}
                                style={inputStyle}
                            />
                        ))}
                        {field('Total do documento', (
                            <MoneyInput
                                data-field="grossAmount"
                                value={document.grossAmount ?? ''}
                                disabled={readOnly}
                                onChange={v => onFieldChange({
                                    grossAmount: v === '' ? null : Number(v)
                                })}
                                style={inputStyle}
                            />
                        ))}
                    </div>

                    {/* Items sum vs document total: the check that keeps group totals honest. */}
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap',
                        fontSize: '0.78rem', color: 'var(--color-text-muted)'
                    }}>
                        <FileText size={13} />
                        Soma dos itens: <strong style={{ color: 'var(--color-text-main)' }}>
                            {formatCurrencyAO(document.itemsTotal)} {document.currency ?? ''}
                        </strong>
                        <span>·</span>
                        {document.items.length} item(ns)
                    </div>

                    {/* Items belonging to THIS document, supplied by the parent. */}
                    {children}

                    {/* Concise, inline — a validation error the user must fix right now. In editor
                        mode the footer owns this list, and stating it twice would read as two
                        different problems. */}
                    {!isEditor && document.validationMessages.length > 0 && (
                        <ul style={{
                            margin: 0, paddingLeft: '18px', fontSize: '0.75rem',
                            color: '#b45309', fontWeight: 600
                        }}>
                            {document.validationMessages.map(m => <li key={m}>{m}</li>)}
                        </ul>
                    )}

                    {!readOnly && (
                        <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                            {showDuplicate && (
                                <button type="button" onClick={onDuplicate} style={linkButton}>
                                    <Copy size={13} /> Duplicar dados básicos
                                </button>
                            )}
                            <button
                                type="button"
                                onClick={onRemove}
                                style={{ ...linkButton, color: '#b91c1c' }}
                            >
                                <Trash2 size={13} /> Remover documento
                            </button>
                            {isSaving && (
                                <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                    A guardar…
                                </span>
                            )}
                        </div>
                    )}

                    {footer}
                </div>
            )}
        </div>
    );
}

const linkButton: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '4px',
    background: 'none', border: 'none', cursor: 'pointer',
    color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.75rem', padding: 0
};
