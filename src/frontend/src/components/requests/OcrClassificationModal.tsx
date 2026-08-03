import React from 'react';
import { documentTypeLabel, isFiscalDocument, normalizeDocumentType } from '../../lib/sourceDocumentType';
import { OcrDocumentClassification } from '../../lib/documentClassificationDecision';

/** Shared row used by both the suggestion modal and the conflict comparison. */
export function ClassificationDetailRow({ label, children }: { label: string; children: React.ReactNode }) {
    return (
        <div style={{ display: 'flex', gap: '10px', fontSize: '0.8rem', lineHeight: 1.5 }}>
            <span style={{
                flex: '0 0 128px', color: 'var(--color-text-muted)', fontWeight: 600
            }}>
                {label}
            </span>
            <span style={{ flex: 1, minWidth: 0, color: 'var(--color-text-main)' }}>{children}</span>
        </div>
    );
}

export function FiscalBadge({ type }: { type?: string | null }) {
    const key = normalizeDocumentType(type);
    if (!key) return null;

    const fiscal = isFiscalDocument(key);
    return (
        <span style={{
            fontSize: '0.65rem', fontWeight: 700, padding: '2px 8px', borderRadius: '999px',
            backgroundColor: fiscal ? '#dcfce7' : '#f1f5f9',
            color: fiscal ? '#15803d' : '#475569',
            border: `1px solid ${fiscal ? '#86efac' : '#cbd5e1'}`,
            whiteSpace: 'nowrap'
        }}>
            {fiscal ? 'Documento fiscal' : 'Não fiscal'}
        </span>
    );
}

/** The evidence block, shared so the suggestion and the conflict always present it identically. */
export function ClassificationEvidence({ ocr }: { ocr: OcrDocumentClassification }) {
    const confidencePct = ocr.confidence != null ? Math.round(ocr.confidence * 100) : null;
    const suggestion = normalizeDocumentType(ocr.suggestedType);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <ClassificationDetailRow label="Documento lido como">
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                    <strong>{documentTypeLabel(suggestion)}</strong>
                    <FiscalBadge type={suggestion} />
                </span>
            </ClassificationDetailRow>

            {confidencePct != null && (
                <ClassificationDetailRow label="Confiança">{confidencePct}%</ClassificationDetailRow>
            )}

            <ClassificationDetailRow label="Título lido">
                {ocr.titleFound
                    ? <strong>{ocr.titleFound}</strong>
                    : <em style={{ color: 'var(--color-text-muted)' }}>Nenhum título foi lido no documento.</em>}
            </ClassificationDetailRow>

            <ClassificationDetailRow label="Evidência">
                {ocr.supportingEvidence?.length
                    ? ocr.supportingEvidence.join('; ')
                    : <em style={{ color: 'var(--color-text-muted)' }}>Sem evidência registada.</em>}
            </ClassificationDetailRow>

            {!!ocr.conflictingEvidence?.length && (
                <ClassificationDetailRow label="Evidência contrária">
                    {ocr.conflictingEvidence.join('; ')}
                </ClassificationDetailRow>
            )}

            {!!ocr.fiscalMarkers?.length && (
                <ClassificationDetailRow label="Marcas fiscais">
                    {ocr.fiscalMarkers.join('; ')}
                </ClassificationDetailRow>
            )}

            {!!ocr.nonFiscalMarkers?.length && (
                <ClassificationDetailRow label="Marcas não fiscais">
                    {ocr.nonFiscalMarkers.join('; ')}
                </ClassificationDetailRow>
            )}

            <ClassificationDetailRow label="Origem">
                {ocr.isFallback
                    ? 'Sugestão do Portal — baseada apenas em indícios (prefixo do número, nome do ficheiro), não na leitura do documento.'
                    : 'Leitura estruturada do documento (OCR).'}
            </ClassificationDetailRow>
        </div>
    );
}

/**
 * What the Portal read, and why — on demand.
 *
 * <p>This used to be a paragraph wedged under a 180px-wide select, where evidence strings wrapped
 * to four lines and pushed the form around. Here the same information is legible, and the field it
 * describes keeps its height.</p>
 *
 * <p>It ends by saying plainly that nothing was selected on the user's behalf, because that is the
 * property the whole design depends on: a reading is a proposal, and only a person classifies.</p>
 */
export function OcrClassificationModalBody({ ocr }: { ocr: OcrDocumentClassification }) {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-muted)' }}>
                O documento anexado foi analisado e o Portal chegou à leitura abaixo.
            </p>

            <ClassificationEvidence ocr={ocr} />

            <p style={{
                margin: 0, padding: '10px 12px', fontSize: '0.78rem', lineHeight: 1.55,
                borderRadius: '8px', border: '1px solid var(--color-border)',
                backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
            }}>
                <strong>Nada foi selecionado automaticamente.</strong> Esta leitura é apenas uma
                proposta: confirme-a escolhendo o mesmo tipo, ou corrija-a escolhendo outro. Se
                escolher um tipo diferente, será pedida uma confirmação e, nos casos de maior risco,
                uma justificativa registada para auditoria.
            </p>
        </div>
    );
}
