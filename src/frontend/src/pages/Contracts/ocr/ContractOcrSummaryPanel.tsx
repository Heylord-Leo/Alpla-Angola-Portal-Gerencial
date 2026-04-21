/**
 * ContractOcrSummaryPanel
 *
 * A collapsible sidebar-style panel that shows all OCR extracted fields
 * grouped by category. Rendered outside the main form grid — typically
 * placed to the right of the form or below it in mobile view.
 *
 * Sections:
 *   1. AUTO_FILL fields — confident extractions with Confirmar/Limpar
 *   2. SUGGESTION fields — lower confidence, shown as chips
 *   3. REFERENCE_ONLY fields — informational data never applied to the form
 *      (Termination Clauses, Signatories, External Reference)
 *
 * Also provides a "Confirmar todos" button that delegates to ocr.confirmAll().
 *
 * Field names displayed here are Portuguese labels, not backend camelCase.
 */

import { useState } from 'react';
import {
    FileSearch, ChevronDown, ChevronUp, CheckCircle, X,
    Cpu, AlertTriangle, Info, Check, Sparkles
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import type { OcrState, OcrExtractedField } from '../../../types/contractOcr.types';

// ─── Friendly Portuguese labels for backend fieldNames ────────────────────────
const FIELD_LABELS: Record<string, string> = {
    // AUTO_FILL — backend fieldNames
    EffectiveDateUtc:          'Data de Início',
    ExpirationDateUtc:         'Data de Validade',
    SignedAtUtc:               'Data de Assinatura',
    TotalContractValue:        'Valor Total do Contrato',
    CurrencyId:                'Moeda',
    // SUGGESTION — backend fieldNames (must match BuildFieldRows in the processor)
    Title:                     'Título',
    CounterpartyName:          'Contraparte',
    GoverningLaw:              'Lei Aplicável',
    PaymentTerms:              'Condições de Pagamento',
    Description:               'Descrição',
    SupplierId:                'Fornecedor',
    // Phase 1.1 — derived financial fields
    SuggestedTotalContractValue: 'Valor Total Sugerido (Calculado)',
    // REFERENCE_ONLY
    TerminationClausesText:    'Cláusulas de Rescisão',
    SignatoryNames:            'Signatários',
    ExternalContractReference: 'Referência Externa',
    RecurringMonthlyAmount:    'Valor Mensal Detectado',
    ContractDurationMonths:    'Prazo (Meses)',
    FinancialInconsistencyWarning: 'Inconsistência Financeira',
};

function label(fieldName: string): string {
    return FIELD_LABELS[fieldName] ?? fieldName;
}

function confidenceColor(score: number | null): string {
    if (score == null) return '#94a3b8';
    if (score >= 0.80) return '#059669';
    if (score >= 0.65) return '#d97706';
    return '#dc2626';
}

// ─── Financial inconsistency warning banner ───────────────────────────────────
/**
 * Rendered at the top of the OCR panel whenever FinancialInconsistencyWarning
 * is present in the reference fields. The banner is intentionally prominent.
 */
function FinancialInconsistencyBanner({ writtenAmountText }: { writtenAmountText?: string | null }) {
    return (
        <div style={{
            padding:         '10px 12px',
            marginBottom:    '14px',
            backgroundColor: '#fff7ed',
            border:          '2px solid #f97316',
            borderRadius:    'var(--radius-sm)',
            display:         'flex',
            gap:             '10px',
            alignItems:      'flex-start',
        }}>
            <AlertTriangle size={16} color="#c2410c" style={{ flexShrink: 0, marginTop: 2 }} />
            <div>
                <div style={{ fontSize: '0.72rem', fontWeight: 800, color: '#c2410c', textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: '4px' }}>
                    Inconsistência Financeira Detectada
                </div>
                <div style={{ fontSize: '0.78rem', color: '#7c2d12', lineHeight: 1.45 }}>
                    O valor por extenso não corresponde ao valor numérico no documento.
                    O valor total <strong>não foi preenchido automaticamente</strong>.
                    Reveja o documento antes de guardar.
                </div>
                {writtenAmountText && (
                    <div style={{ marginTop: '6px', fontSize: '0.72rem', color: '#9a3412', fontStyle: 'italic' }}>
                        Valor por extenso: "{writtenAmountText}"
                    </div>
                )}
            </div>
        </div>
    );
}

// ─── Auto-fill field row ──────────────────────────────────────────────────────
interface AutoFillRowProps {
    fieldName: string;
    normalisedValue: string | null;
    rawValue: string | null;
    confidenceScore: number | null;
    confirmed: boolean;
    onConfirm: () => void;
    onDiscard: () => void;
}

function AutoFillRow({ fieldName, normalisedValue, rawValue, confidenceScore, confirmed, onConfirm, onDiscard }: AutoFillRowProps) {
    const displayVal = normalisedValue ?? rawValue ?? '—';
    const pct        = confidenceScore != null ? Math.round(confidenceScore * 100) : null;
    const dotColor   = confidenceColor(confidenceScore);

    return (
        <div style={{
            padding:         '8px 10px',
            borderRadius:    'var(--radius-sm)',
            backgroundColor: confirmed ? '#f0fdf4' : '#fffbeb',
            border:          `1px solid ${confirmed ? '#86efac' : '#fde68a'}`,
            marginBottom:    '6px',
        }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '6px' }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: '0.65rem', fontWeight: 700, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: '2px' }}>
                        {label(fieldName)}
                    </div>
                    <div style={{
                        fontSize:     '0.8rem',
                        fontWeight:   600,
                        color:        '#1e293b',
                        overflow:     'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace:   'nowrap',
                    }} title={displayVal}>
                        {displayVal}
                    </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                    {pct != null && (
                        <span style={{
                            fontSize:        '0.6rem',
                            fontWeight:      700,
                            color:           dotColor,
                            backgroundColor: dotColor + '1a',
                            padding:         '1px 6px',
                            borderRadius:    '100px',
                        }}>
                            {pct}%
                        </span>
                    )}

                    {!confirmed ? (
                        <>
                            <button type="button" onClick={onConfirm}
                                title="Confirmar"
                                style={{
                                    background: 'none', border: 'none', cursor: 'pointer',
                                    color: '#d97706', padding: '2px', display: 'flex',
                                    transition: 'color 0.15s',
                                }}
                                onMouseOver={e => (e.currentTarget.style.color = '#b45309')}
                                onMouseOut={e  => (e.currentTarget.style.color = '#d97706')}
                            >
                                <CheckCircle size={15} strokeWidth={2} />
                            </button>
                            <button type="button" onClick={onDiscard}
                                title="Limpar"
                                style={{
                                    background: 'none', border: 'none', cursor: 'pointer',
                                    color: '#94a3b8', padding: '2px', display: 'flex',
                                    transition: 'color 0.15s',
                                }}
                                onMouseOver={e => (e.currentTarget.style.color = '#dc2626')}
                                onMouseOut={e  => (e.currentTarget.style.color = '#94a3b8')}
                            >
                                <X size={14} strokeWidth={2} />
                            </button>
                        </>
                    ) : (
                        <CheckCircle size={15} color="#059669" strokeWidth={2.5} />
                    )}
                </div>
            </div>
        </div>
    );
}

// ─── Suggestion field row ────────────────────────────────────────────────────
interface SuggestionRowProps {
    field:             OcrExtractedField;
    onApply:           () => void;
    onDiscard:         () => void;
    isSupplierField?:  boolean;
    applied:           boolean;
}

function SuggestionRow({ field, onApply, onDiscard, isSupplierField = false, applied }: SuggestionRowProps) {
    const displayVal = field.normalisedValue ?? field.rawExtractedValue ?? '—';
    const pct = field.confidenceScore != null ? Math.round(field.confidenceScore * 100) : null;

    return (
        <div style={{
            padding:         '7px 10px',
            borderRadius:    'var(--radius-sm)',
            backgroundColor: applied ? '#f0fdf4' : field.discardedByUser ? '#f8fafc' : '#eff6ff',
            border:          `1px solid ${applied ? '#86efac' : field.discardedByUser ? '#e2e8f0' : '#bfdbfe'}`,
            marginBottom:    '5px',
            opacity:         field.discardedByUser ? 0.5 : 1,
            transition:      'background-color 0.2s, border-color 0.2s',
        }}>
            {/* Label + confidence */}
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '4px' }}>
                <div style={{ fontSize: '0.65rem', fontWeight: 700, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.06em' }}>
                    {label(field.fieldName)}
                </div>
                {pct != null && (
                    <span style={{
                        fontSize:        '0.6rem',
                        fontWeight:      700,
                        color:           '#2563eb',
                        backgroundColor: '#dbeafe',
                        padding:         '1px 6px',
                        borderRadius:    '100px',
                    }}>
                        {pct}%
                    </span>
                )}
            </div>

            {/* Value */}
            <div style={{
                fontSize:     '0.78rem',
                color:        '#1e293b',
                fontWeight:   500,
                overflow:     'hidden',
                textOverflow: 'ellipsis',
                whiteSpace:   'nowrap',
                marginBottom: field.discardedByUser || applied ? '0' : '7px',
            }} title={displayVal}>
                {displayVal}
            </div>

            {/* Actions — hidden once applied or discarded */}
            {!field.discardedByUser && !applied && (
                <div style={{ display: 'flex', gap: '5px', alignItems: 'center' }}>
                    <button
                        type="button"
                        onClick={onApply}
                        style={{
                            display:         'flex',
                            alignItems:      'center',
                            gap:             '3px',
                            padding:         '2px 9px',
                            fontSize:        '0.68rem',
                            fontWeight:      700,
                            backgroundColor: '#2563eb',
                            color:           'white',
                            border:          'none',
                            borderRadius:    'var(--radius-sm)',
                            cursor:          'pointer',
                            whiteSpace:      'nowrap',
                            transition:      'background-color 0.15s',
                        }}
                        onMouseOver={e => (e.currentTarget.style.backgroundColor = '#1d4ed8')}
                        onMouseOut={e  => (e.currentTarget.style.backgroundColor = '#2563eb')}
                    >
                        <Check size={10} strokeWidth={2.5} />
                        {isSupplierField ? 'Usar como pesquisa' : 'Aplicar'}
                    </button>

                    <button
                        type="button"
                        onClick={onDiscard}
                        style={{
                            display:         'flex',
                            alignItems:      'center',
                            gap:             '3px',
                            padding:         '2px 7px',
                            fontSize:        '0.68rem',
                            fontWeight:      600,
                            backgroundColor: 'transparent',
                            color:           '#64748b',
                            border:          '1px solid #bfdbfe',
                            borderRadius:    'var(--radius-sm)',
                            cursor:          'pointer',
                            whiteSpace:      'nowrap',
                            transition:      'opacity 0.15s',
                        }}
                        onMouseOver={e => (e.currentTarget.style.opacity = '0.6')}
                        onMouseOut={e  => (e.currentTarget.style.opacity = '1')}
                    >
                        <X size={10} strokeWidth={2} />
                        Ignorar
                    </button>

                    {isSupplierField && (
                        <Sparkles size={11} color="#2563eb" strokeWidth={2} style={{ marginLeft: '2px' }} />
                    )}
                </div>
            )}

            {/* Applied confirmation tick */}
            {applied && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', marginTop: '3px', fontSize: '0.67rem', color: '#059669', fontWeight: 600 }}>
                    <CheckCircle size={11} strokeWidth={2.5} />
                    {isSupplierField ? 'Pesquisa preenchida' : 'Aplicado ao formulário'}
                </div>
            )}
        </div>
    );
}

// ─── Reference-only field row ─────────────────────────────────────────────────
function ReferenceRow({ field }: { field: OcrExtractedField }) {
    const [expanded, setExpanded] = useState(false);
    const val = field.normalisedValue ?? field.rawExtractedValue ?? '—';
    const isLong = val.length > 120;

    return (
        <div style={{
            padding:         '8px 10px',
            borderRadius:    'var(--radius-sm)',
            backgroundColor: '#f8fafc',
            border:          '1px solid #e2e8f0',
            marginBottom:    '6px',
        }}>
            <div style={{ fontSize: '0.65rem', fontWeight: 700, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: '4px' }}>
                {label(field.fieldName)}
            </div>
            <div style={{
                fontSize:   '0.78rem',
                color:      '#475569',
                lineHeight: 1.5,
                overflow:   isLong && !expanded ? 'hidden' : undefined,
                display:    isLong && !expanded ? '-webkit-box' : undefined,
                WebkitLineClamp: isLong && !expanded ? 3 : undefined,
                WebkitBoxOrient: isLong && !expanded ? 'vertical' : undefined,
                whiteSpace: 'pre-wrap',
            }}>
                {val}
            </div>
            {isLong && (
                <button type="button" onClick={() => setExpanded(v => !v)}
                    style={{
                        marginTop:   '4px',
                        background:  'none',
                        border:      'none',
                        cursor:      'pointer',
                        fontSize:    '0.67rem',
                        fontWeight:  600,
                        color:       'var(--color-primary)',
                        padding:     '0',
                        display:     'flex',
                        alignItems:  'center',
                        gap:         '2px',
                    }}
                >
                    {expanded ? <><ChevronUp size={11} /> Mostrar menos</> : <><ChevronDown size={11} /> Mostrar mais</>}
                </button>
            )}
        </div>
    );
}

// ─── Main Panel ───────────────────────────────────────────────────────────────

/** Suggestion fields backed by real form setters — SupplierId is the supplier-search special case */
const SUPPLIER_SUGGESTION_FIELD = 'SupplierId';

interface ContractOcrSummaryPanelProps {
    ocrState: OcrState;
    referenceFields: OcrExtractedField[];
    qualityScore: number | null;
    isPartial: boolean;
    conflictsDetected: boolean;
    onConfirmField: (fieldName: string) => Promise<void>;
    onDiscardField: (fieldName: string) => Promise<void>;
    onConfirmAll: () => Promise<void>;
    /** True when COMPLETED */
    isVisible: boolean;
    /**
     * Called when the user clicks "Aplicar" on a SUGGESTION row.
     * The panel passes the fieldName and the suggested value.
     * ContractCreate uses this to write into the correct form state setter.
     * For SupplierId, the parent pre-fills the autocomplete search text instead
     * of setting supplierId directly.
     */
    onApplySuggestion: (fieldName: string, value: string) => void;
    /** fieldNames that have already been applied (to show applied state in panel) */
    appliedSuggestions: Set<string>;
}

export function ContractOcrSummaryPanel({
    ocrState,
    referenceFields,
    qualityScore,
    isPartial,
    conflictsDetected,
    onConfirmField,
    onDiscardField,
    onConfirmAll,
    isVisible,
    onApplySuggestion,
    appliedSuggestions,
}: ContractOcrSummaryPanelProps) {
    const [open, setOpen] = useState(true);

    const autoFillFields   = Object.values(ocrState).filter(f => f.displayHint === 'AUTO_FILL');
    const suggestionFields = Object.values(ocrState).filter(f => f.displayHint === 'SUGGESTION');
    const unconfirmedCount = autoFillFields.filter(f => !f.confirmed && !f.discarded).length;

    // Phase 1.1 — extract inconsistency warning from reference fields
    const financialInconsistencyField = referenceFields.find(
        f => f.fieldName === 'FinancialInconsistencyWarning'
    );
    // Filter it out of the generic reference list so it does NOT appear as a normal row
    const visibleReferenceFields = referenceFields.filter(
        f => f.fieldName !== 'FinancialInconsistencyWarning'
    );

    if (!isVisible) return null;

    const pct = qualityScore != null ? Math.round(qualityScore * 100) : null;

    return (
        <AnimatePresence>
            <motion.div
                key="ocr-panel"
                initial={{ opacity: 0, x: 16 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 16 }}
                transition={{ duration: 0.22 }}
                style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    border:          '1px solid #e2e8f0',
                    borderRadius:    'var(--radius-lg)',
                    overflow:        'hidden',
                    boxShadow:       'var(--shadow-sm)',
                }}
            >
                {/* ── Panel header ──────────────────────────────────────────── */}
                <button
                    type="button"
                    onClick={() => setOpen(v => !v)}
                    style={{
                        width:           '100%',
                        display:         'flex',
                        alignItems:      'center',
                        justifyContent:  'space-between',
                        padding:         '12px 16px',
                        background:      open
                            ? 'linear-gradient(135deg, rgba(var(--color-primary-rgb),0.06) 0%, #eff6ff 100%)'
                            : 'transparent',
                        border:          'none',
                        borderBottom:    open ? '1px solid #e2e8f0' : 'none',
                        cursor:          'pointer',
                        textAlign:       'left',
                        transition:      'background 0.2s',
                    }}
                >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Cpu size={16} color="var(--color-primary)" strokeWidth={2} />
                        <span style={{ fontSize: '0.8rem', fontWeight: 800, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '0.07em' }}>
                            Campos OCR
                        </span>
                        {unconfirmedCount > 0 && (
                            <span style={{
                                backgroundColor: '#d97706',
                                color:           'white',
                                padding:         '1px 7px',
                                fontSize:        '0.6rem',
                                fontWeight:      900,
                                borderRadius:    '100px',
                            }}>
                                {unconfirmedCount} pendente{unconfirmedCount !== 1 ? 's' : ''}
                            </span>
                        )}
                    </div>
                    {open ? <ChevronUp size={16} color="#64748b" /> : <ChevronDown size={16} color="#64748b" />}
                </button>

                <AnimatePresence initial={false}>
                    {open && (
                        <motion.div
                            initial={{ height: 0, opacity: 0 }}
                            animate={{ height: 'auto', opacity: 1 }}
                            exit={{ height: 0, opacity: 0 }}
                            transition={{ duration: 0.2 }}
                            style={{ overflow: 'hidden' }}
                        >
                            <div style={{ padding: '14px 16px' }}>

                                {/* Quality + flags row */}
                                <div style={{
                                    display:         'flex',
                                    gap:             '8px',
                                    flexWrap:        'wrap',
                                    marginBottom:    '14px',
                                }}>
                                    {pct != null && (
                                        <span style={{
                                            display:         'flex',
                                            alignItems:      'center',
                                            gap:             '4px',
                                            padding:         '3px 10px',
                                            fontSize:        '0.68rem',
                                            fontWeight:      700,
                                            borderRadius:    '100px',
                                            backgroundColor: pct >= 75 ? '#d1fae5' : pct >= 55 ? '#fef3c7' : '#fee2e2',
                                            color:           pct >= 75 ? '#065f46' : pct >= 55 ? '#92400e' : '#991b1b',
                                            border:          `1px solid ${pct >= 75 ? '#6ee7b7' : pct >= 55 ? '#fcd34d' : '#fca5a5'}`,
                                        }}>
                                            <FileSearch size={10} strokeWidth={2.5} />
                                            Qualidade {pct}%
                                        </span>
                                    )}
                                    {isPartial && (
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '3px', padding: '3px 9px', fontSize: '0.66rem', fontWeight: 700, borderRadius: '100px', backgroundColor: '#fef3c7', color: '#92400e', border: '1px solid #fcd34d' }}>
                                            <AlertTriangle size={9} strokeWidth={2.5} /> Extracção parcial
                                        </span>
                                    )}
                                    {conflictsDetected && (
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '3px', padding: '3px 9px', fontSize: '0.66rem', fontWeight: 700, borderRadius: '100px', backgroundColor: '#fef2f2', color: '#b91c1c', border: '1px solid #fca5a5' }}>
                                            <AlertTriangle size={9} strokeWidth={2.5} /> Conflitos detectados
                                        </span>
                                    )}
                                </div>

                                {/* Confirm all button */}
                                {unconfirmedCount > 0 && (
                                    <button
                                        type="button"
                                        onClick={onConfirmAll}
                                        style={{
                                            width:           '100%',
                                            display:         'flex',
                                            alignItems:      'center',
                                            justifyContent:  'center',
                                            gap:             '6px',
                                            padding:         '7px',
                                            fontSize:        '0.78rem',
                                            fontWeight:      700,
                                            backgroundColor: '#fffbeb',
                                            color:           '#92400e',
                                            border:          '1px solid #fde68a',
                                            borderRadius:    'var(--radius-md)',
                                            cursor:          'pointer',
                                            marginBottom:    '12px',
                                            transition:      'background-color 0.15s',
                                        }}
                                        onMouseOver={e => (e.currentTarget.style.backgroundColor = '#fef3c7')}
                                        onMouseOut={e  => (e.currentTarget.style.backgroundColor = '#fffbeb')}
                                    >
                                        <CheckCircle size={14} strokeWidth={2.5} />
                                        Confirmar todos ({unconfirmedCount})
                                    </button>
                                )}

                                {/* Financial inconsistency banner — rendered before any field section */}
                                {financialInconsistencyField && (
                                    <FinancialInconsistencyBanner
                                        writtenAmountText={financialInconsistencyField.rawExtractedValue}
                                    />
                                )}

                                {/* AUTO_FILL section */}
                                {autoFillFields.length > 0 && (
                                    <div style={{ marginBottom: '12px' }}>
                                        <div style={{ fontSize: '0.65rem', fontWeight: 800, color: '#d97706', textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: '8px' }}>
                                            Preenchimento automático
                                        </div>
                                        {autoFillFields.map(f => (
                                            <AutoFillRow
                                                key={f.fieldName}
                                                fieldName={f.fieldName}
                                                normalisedValue={f.normalisedValue}
                                                rawValue={f.rawValue}
                                                confidenceScore={f.confidenceScore}
                                                confirmed={f.confirmed}
                                                onConfirm={() => onConfirmField(f.fieldName)}
                                                onDiscard={() => onDiscardField(f.fieldName)}
                                            />
                                        ))}
                                    </div>
                                )}

                                {/* SUGGESTION section */}
                                {suggestionFields.length > 0 && (
                                    <div style={{ marginBottom: '12px' }}>
                                        <div style={{ fontSize: '0.65rem', fontWeight: 800, color: '#2563eb', textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: '8px' }}>
                                            Sugestões
                                        </div>
                                        {suggestionFields.map(f => {
                                            const suggestedValue = f.normalisedValue ?? f.rawValue ?? '';
                                            const isSupplier = f.fieldName === SUPPLIER_SUGGESTION_FIELD;
                                            const isApplied  = appliedSuggestions.has(f.fieldName);
                                            // Build a minimal OcrExtractedField-compatible shape for SuggestionRow
                                            const rowField: OcrExtractedField = {
                                                id:                f.fieldId,
                                                fieldName:         f.fieldName,
                                                displayHint:       f.displayHint,
                                                normalisedValue:   f.normalisedValue,
                                                rawExtractedValue: f.rawValue,
                                                confidenceScore:   f.confidenceScore,
                                                confirmedByUser:   f.confirmed,
                                                confirmedAtUtc:    null,
                                                wasOverridden:     false,
                                                discardedByUser:   f.discarded,
                                                finalSavedValue:   f.overriddenValue ?? null,
                                            };
                                            return (
                                                <SuggestionRow
                                                    key={f.fieldName}
                                                    field={rowField}
                                                    applied={isApplied}
                                                    isSupplierField={isSupplier}
                                                    onApply={() => onApplySuggestion(f.fieldName, suggestedValue)}
                                                    onDiscard={() => onDiscardField(f.fieldName)}
                                                />
                                            );
                                        })}
                                    </div>
                                )}

                                {/* REFERENCE_ONLY section */}
                                {visibleReferenceFields.length > 0 && (
                                    <div>
                                        <div style={{
                                            display:      'flex',
                                            alignItems:   'center',
                                            gap:          '5px',
                                            fontSize:     '0.65rem',
                                            fontWeight:   800,
                                            color:        '#64748b',
                                            textTransform: 'uppercase',
                                            letterSpacing: '0.08em',
                                            marginBottom: '8px',
                                        }}>
                                            <Info size={10} strokeWidth={2.5} />
                                            Referência (não aplicado ao formulário)
                                        </div>
                                        {visibleReferenceFields.map(f => (
                                            <ReferenceRow key={f.fieldName} field={f} />
                                        ))}
                                    </div>
                                )}
                            </div>
                        </motion.div>
                    )}
                </AnimatePresence>
            </motion.div>
        </AnimatePresence>
    );
}
