import React from 'react';
import { AlertTriangle, CheckCircle, ShieldAlert, ShieldCheck, Wand2 } from 'lucide-react';
import {
    PoDocumentIdentification, PoNumberField, describePoDocumentReview, poNumberChanged, poNumberSourceLabel, numberDecisionPending,
} from '../../lib/poDocumentIdentification';

/**
 * v2.249.4 — the two blocks both P.O. modals share: the P.O. NUMBER field (current vs proposed, source badge, suggestion)
 * and the DOCUMENT VERDICT (identified / uncertain + explicit confirmation / other type = blocked). Presentation only:
 * every rule lives in lib/poDocumentIdentification and is re-applied by the server.
 */

const inputStyle: React.CSSProperties = {
    width: '100%', padding: '12px 14px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)',
    borderRadius: 'var(--radius-sm)', fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', fontFamily: 'inherit',
};

export function PoNumberFieldBlock({ field, currentNumber, disabled, inputId, onChange, onUseSuggestion, onKeepCurrent, children }: {
    field: PoNumberField;
    /** The group's number today (correction only). Null/undefined for the first registration. */
    currentNumber?: string | null;
    disabled?: boolean;
    inputId?: string;
    onChange: (value: string) => void;
    onUseSuggestion: () => void;
    /** Correction only: the explicit "keep the current number" decision. */
    onKeepCurrent?: () => void;
    children?: React.ReactNode;
}) {
    const isCorrection = !!(currentNumber && currentNumber.trim());
    const changed = isCorrection && poNumberChanged(currentNumber, field.value);
    const suggestionDiffers = !!field.suggestion && field.suggestion.trim().toUpperCase() !== field.value.trim().toUpperCase();
    const decisionPending = numberDecisionPending(field, currentNumber);
    const suggestionDiffersFromCurrent = isCorrection && !!field.suggestion && poNumberChanged(currentNumber, field.suggestion);
    return (
        <div data-testid="po-number-block" style={{ marginBottom: '20px' }}>
            {isCorrection && (
                <div data-testid="po-number-current" style={{ display: 'flex', gap: '12px', alignItems: 'baseline', marginBottom: '10px', fontSize: '0.8rem' }}>
                    <span style={{ fontWeight: 800, textTransform: 'uppercase', fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>Número atual</span>
                    <span style={{ fontWeight: 800, color: 'var(--color-text-main)', textDecoration: changed ? 'line-through' : 'none' }}>{currentNumber}</span>
                    {changed && <span style={{ fontSize: '0.7rem', fontWeight: 800, color: '#b45309', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '10px', padding: '1px 8px' }}>será substituído</span>}
                </div>
            )}
            <label htmlFor={inputId} style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                {isCorrection ? 'Número da P.O. corrigida' : 'Número da P.O'} <span style={{ color: '#ef4444' }}>*</span>
                <span data-testid="po-number-source" style={{ fontSize: '0.65rem', padding: '2px 8px', borderRadius: '999px', fontWeight: 700, textTransform: 'none',
                    backgroundColor: field.source === 'EXTRACTED' ? 'rgba(22,163,74,0.1)' : field.source === 'MANUAL' ? 'rgba(37,99,235,0.1)' : 'rgba(100,116,139,0.12)',
                    color: field.source === 'EXTRACTED' ? '#16a34a' : field.source === 'MANUAL' ? '#1d4ed8' : '#475569' }}>
                    {poNumberSourceLabel(field.source)}
                </span>
            </label>
            {/* v2.249.4 — correction: the document reads a different number → an explicit choice is required before submission */}
            {suggestionDiffersFromCurrent && (
                <div role={decisionPending ? 'alert' : undefined} data-testid="po-number-decision" data-pending={decisionPending ? 'true' : 'false'}
                     style={{ marginBottom: '10px', padding: '12px 14px', backgroundColor: decisionPending ? '#fffbeb' : '#f8fafc', border: `2px solid ${decisionPending ? '#f59e0b' : 'var(--color-border)'}`, borderRadius: '8px', fontSize: '0.8rem' }}>
                    <div style={{ fontWeight: 800, color: decisionPending ? '#92400e' : 'var(--color-text-main)', marginBottom: '8px' }}>
                        {decisionPending ? 'O documento indica um número diferente do atual — escolha antes de registar' : field.currentConfirmed ? 'Número atual mantido por decisão do comprador (o documento indica outro número)' : 'Número do documento adotado'}
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '10px' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            <span style={{ fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Número atual do grupo</span>
                            <strong data-testid="po-number-decision-current">{currentNumber}</strong>
                            {onKeepCurrent && (
                                <button type="button" data-testid="po-number-keep-current" onClick={onKeepCurrent} disabled={disabled} style={{ alignSelf: 'flex-start', border: '1px solid var(--color-border)', background: field.currentConfirmed ? 'var(--color-bg-page)' : '#fff', color: 'var(--color-text-main)', borderRadius: '6px', padding: '4px 10px', fontWeight: 800, fontSize: '0.72rem', cursor: 'pointer' }}>
                                    {field.currentConfirmed ? 'Número atual mantido ✓' : 'Manter número atual'}
                                </button>
                            )}
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            <span style={{ fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Detetado no documento</span>
                            <strong data-testid="po-number-decision-detected">{field.suggestion}{field.suggestionFamilyLabel ? ` (${field.suggestionFamilyLabel})` : ' (série não reconhecida)'}</strong>
                            <button type="button" data-testid="po-number-use-detected" onClick={onUseSuggestion} disabled={disabled} style={{ alignSelf: 'flex-start', display: 'inline-flex', alignItems: 'center', gap: '4px', border: '1px solid var(--color-primary)', background: field.source === 'EXTRACTED' ? 'rgba(var(--color-primary-rgb), 0.08)' : 'transparent', color: 'var(--color-primary)', borderRadius: '6px', padding: '4px 10px', fontWeight: 800, fontSize: '0.72rem', cursor: 'pointer' }}>
                                <Wand2 size={12} /> {field.source === 'EXTRACTED' ? 'Número detetado adotado ✓' : 'Usar número detetado'}
                            </button>
                        </div>
                    </div>
                    <div style={{ marginTop: '8px', color: 'var(--color-text-muted)' }}>Também pode escrever outro número no campo abaixo. Manter o número atual quando o documento lê outro fica registado como divergência confirmada.</div>
                </div>
            )}
            <input id={inputId} type="text" value={field.value} disabled={disabled} onChange={e => onChange(e.target.value)} placeholder="Ex: ECF11 2026/421" style={inputStyle} />
            {field.suggestion && suggestionDiffers && !suggestionDiffersFromCurrent && (
                <div data-testid="po-number-suggestion" style={{ marginTop: '8px', display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap', fontSize: '0.78rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                    <span>Extraído do documento: <strong style={{ color: 'var(--color-text-main)' }}>{field.suggestion}</strong>{field.suggestionFamilyLabel ? ` (${field.suggestionFamilyLabel})` : ''}</span>
                    <button type="button" onClick={onUseSuggestion} disabled={disabled} style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', border: '1px solid var(--color-primary)', background: 'transparent', color: 'var(--color-primary)', borderRadius: '6px', padding: '4px 10px', fontWeight: 800, fontSize: '0.72rem', cursor: 'pointer' }}>
                        <Wand2 size={12} /> Usar número extraído
                    </button>
                </div>
            )}
            {children}
        </div>
    );
}

export function PoDocumentVerdictBlock({ identification, hasBusinessMismatches, typeConfirmed, onTypeConfirmedChange, idPrefix, detectedNumber }: {
    identification: PoDocumentIdentification | null;
    hasBusinessMismatches: boolean;
    typeConfirmed: boolean;
    onTypeConfirmedChange: (value: boolean) => void;
    idPrefix: string;
    /** The raw document number the extraction read (shown for review even when the series is not recognised, e.g. ECF99). */
    detectedNumber?: string | null;
}) {
    const review = describePoDocumentReview(identification, hasBusinessMismatches);
    const palette = review.tone === 'success'
        ? { bg: '#f0fdf4', border: '#22c55e', title: '#16a34a', text: '#15803d', Icon: CheckCircle }
        : review.tone === 'warning'
            ? { bg: '#fffbeb', border: '#f59e0b', title: '#92400e', text: '#78350f', Icon: AlertTriangle }
            : { bg: '#fef2f2', border: '#ef4444', title: '#991b1b', text: '#b91c1c', Icon: ShieldAlert };
    const { Icon } = palette;
    const detected = identification?.verdict === 'IDENTIFIED_PO' || (identification?.identityCandidates.length ?? 0) > 0;
    return (
        <div role={review.tone === 'blocked' ? 'alert' : undefined} data-testid="po-document-verdict" data-tone={review.tone}
             style={{ padding: '16px 18px', backgroundColor: palette.bg, border: `2px solid ${palette.border}`, borderRadius: '8px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                <Icon size={22} color={palette.title} style={{ flexShrink: 0, marginTop: '1px' }} />
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <span style={{ fontWeight: 800, color: palette.title, fontSize: '0.95rem' }}>{review.title}</span>
                    <span style={{ fontWeight: 600, color: palette.text, fontSize: '0.8rem', lineHeight: 1.45 }}>{review.message}</span>
                </div>
            </div>
            {detected && identification && (
                <div data-testid="po-document-detected" style={{ display: 'flex', flexWrap: 'wrap', gap: '14px', fontSize: '0.75rem', color: palette.text, fontWeight: 600, paddingLeft: '34px' }}>
                    <span>Tipo detetado: <strong>{identification.family ?? identification.identityCandidates[0]?.family ?? '—'}</strong>{(identification.familyLabel ?? identification.identityCandidates[0]?.familyLabel) ? ` — ${identification.familyLabel ?? identification.identityCandidates[0]?.familyLabel}` : ''}</span>
                    <span>Número completo: <strong>{identification.reference ?? identification.identityCandidates.map(c => c.reference).join(' / ')}</strong></span>
                    <span>Origem: extraído do documento</span>
                </div>
            )}
            {identification && identification.verdict !== 'IDENTIFIED_PO' && detectedNumber && (
                <div data-testid="po-document-detected-number" style={{ paddingLeft: '34px', fontSize: '0.75rem', color: palette.text, fontWeight: 600 }}>
                    Número lido no documento: <strong>{detectedNumber}</strong>{identification.identityCandidates.length === 0 ? ' — série não reconhecida (ECF11 = Serviços, ECF10 = Materiais diversos, ECF = Material de stock); confirme e, se correto, use-o como número da P.O.' : ''}
                </div>
            )}
            {identification && identification.identityCandidates.length > 1 && (
                <div style={{ paddingLeft: '34px', fontSize: '0.75rem', color: palette.text, fontWeight: 600 }}>
                    Referências encontradas: {identification.identityCandidates.map(c => `${c.reference} (${c.familyLabel})`).join(' · ')}
                </div>
            )}
            {identification && identification.bodyReferences.length > 0 && (
                <div data-testid="po-document-body-references" style={{ paddingLeft: '34px', fontSize: '0.75rem', color: palette.text, fontWeight: 600 }}>
                    Referências a outras P.O. no corpo do documento (não são a identidade deste documento): {identification.bodyReferences.map(c => c.reference).join(', ')}
                </div>
            )}
            {review.needsTypeConfirmation && (
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fef3c7', padding: '12px', borderRadius: '4px', marginLeft: '34px' }}>
                    <input type="checkbox" id={`${idPrefix}-po-type-confirm`} data-testid="po-type-confirm" checked={typeConfirmed} onChange={e => onTypeConfirmedChange(e.target.checked)} style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }} />
                    <label htmlFor={`${idPrefix}-po-type-confirm`} style={{ cursor: 'pointer', color: '#92400e', fontSize: '0.8rem', fontWeight: 700, lineHeight: 1.4 }}>
                        Confirmo que este documento é uma Ordem de Compra (P.O.) emitida pelo ERP e que o número introduzido é o número completo do documento. A justificativa no comentário é obrigatória e fica registada no histórico.
                    </label>
                </div>
            )}
            {review.tone === 'blocked' && (
                <div style={{ paddingLeft: '34px', fontSize: '0.75rem', color: palette.text, fontWeight: 600, display: 'flex', gap: '6px', alignItems: 'center' }}>
                    <ShieldCheck size={13} /> Escolha outro ficheiro: o registo com este documento é recusado pelo servidor, mesmo com fornecedor e valor coincidentes.
                </div>
            )}
        </div>
    );
}
