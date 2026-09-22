import { useMemo, useState } from 'react';
import { AlertTriangle, FileSearch } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { SourceDocumentTypeField } from './SourceDocumentTypeField';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { mapOperationInvoiceError } from '../../lib/operationInvoiceView';
import { ApiError } from '../../lib/api';
import {
    documentTypeExplanations,
    normalizeDocumentType,
    type DocumentUsageContext
} from '../../lib/sourceDocumentType';
import { RECONCILIATION_JUSTIFICATION_MIN_LENGTH } from '../../lib/reconciliationJustificationValidator';
import type { OperationInvoiceObligationDto } from '../../types/operationInvoice';

interface ClassificationModalProps {
    requestId: string;
    obligation: OperationInvoiceObligationDto;
    /** PAYMENT → payment-origin options; anything else → quotation-origin options (backend mirrors this). */
    requestTypeCode: string | null;
    onClose: () => void;
    /** Called after a successful classification — the caller refreshes coverage, readiness and the request. */
    onClassified: () => void;
}

/**
 * v2.245.8 — "Classificar Documento de Origem": the Finance decision that gives a legacy
 * (UNCLASSIFIED) group its document identity. Reuses the SAME field and option set as the origin
 * screens; the consequence text comes from the shared document-type explanations so this modal can
 * never describe an obligation differently from the rest of the Portal. The backend derives the
 * obligations, captures the expected total (the group's ordered total) and audits the decision.
 */
export function OperationInvoiceClassificationModal({
    requestId, obligation, requestTypeCode, onClose, onClassified
}: ClassificationModalProps) {
    const context: DocumentUsageContext = requestTypeCode === 'PAYMENT' ? 'PAYMENT_REQUEST' : 'QUOTATION_MANAGEMENT';
    const [type, setType] = useState('');
    const [justification, setJustification] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

    const explanation = useMemo(() => {
        const key = normalizeDocumentType(type);
        return key ? documentTypeExplanations(context).find(e => e.value === key) ?? null : null;
    }, [type, context]);

    const justificationTooShort = justification.trim().length < RECONCILIATION_JUSTIFICATION_MIN_LENGTH;
    const canSubmit = !!normalizeDocumentType(type) && !justificationTooShort && !saving;

    const submit = async () => {
        if (!canSubmit) return;
        setSaving(true);
        setError(null);
        setFieldErrors({});
        try {
            await operationInvoiceApi.classifyGroup(requestId, obligation.groupId, {
                sourceDocumentType: normalizeDocumentType(type)!,
                justification: justification.trim()
            });
            onClassified();
        } catch (err) {
            if (err instanceof ApiError && err.fieldErrors) setFieldErrors(err.fieldErrors);
            const mapped = mapOperationInvoiceError(err);
            setError(mapped.isConcurrency
                ? `${mapped.message} Feche e reabra para recarregar os dados.`
                : mapped.message);
        } finally {
            setSaving(false);
        }
    };

    const labelStyle: React.CSSProperties = {
        fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase',
        color: 'var(--color-text-muted)', display: 'block', marginBottom: '4px'
    };
    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
        borderRadius: '8px', fontSize: '0.88rem', boxSizing: 'border-box'
    };
    const fieldError = (key: string) => fieldErrors[key]?.length
        ? <div style={{ color: '#b91c1c', fontSize: '0.75rem', fontWeight: 600, marginTop: '2px' }}>{fieldErrors[key][0]}</div>
        : null;

    return (
        <ModalWrapper title="Classificar Documento de Origem" onClose={onClose} width={620}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ fontWeight: 800 }}>
                    {obligation.supplierName || 'Fornecedor —'}
                    <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', marginLeft: '8px', fontSize: '0.85rem' }}>
                        {obligation.purchaseOrderNumber ? `P.O. ${obligation.purchaseOrderNumber}` : 'Sem P.O.'}
                        {obligation.currency ? ` · ${obligation.currency}` : ''}
                    </span>
                </div>

                <div style={{
                    display: 'flex', gap: '8px', alignItems: 'flex-start', padding: '10px 12px',
                    backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px',
                    fontSize: '0.82rem', color: '#1e40af', fontWeight: 600
                }}>
                    <FileSearch size={16} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <span>
                        Este grupo foi criado antes do fluxo atual e ainda não tem o documento de origem
                        classificado. A classificação identifica o documento que o fornecedor emitiu e
                        determina o que o Portal irá exigir para concluir o grupo. Não altera valores,
                        fornecedor, recebimento nem os documentos já anexados.
                    </span>
                </div>

                <div>
                    <SourceDocumentTypeField
                        context={context}
                        value={type}
                        onChange={setType}
                        required
                        error={fieldErrors['SourceDocumentType']?.[0] ?? null}
                        labelStyle={labelStyle}
                        inputStyle={inputStyle}
                    />
                </div>

                {explanation && (
                    <div style={{
                        display: 'flex', flexDirection: 'column', gap: '4px', padding: '10px 12px',
                        backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '8px',
                        fontSize: '0.82rem', color: '#92400e'
                    }}>
                        <span style={{ fontWeight: 800 }}>Consequência desta classificação</span>
                        <span><b>O que é:</b> {explanation.whatItIs}</span>
                        <span><b>O que será exigido:</b> {explanation.whatComesNext}</span>
                        {/* The expected total is the group's ordered total — stated, not recomputed here. */}
                        <span>
                            <b>Valor esperado da fatura final:</b>{' '}
                            {explanation.value === 'INVOICE'
                                ? 'não aplicável — a factura já documenta a operação.'
                                : `o total do grupo${obligation.currency ? ` (${obligation.currency})` : ''}, fixado nesta classificação e nunca recalculado.`}
                        </span>
                    </div>
                )}

                <div>
                    <label style={labelStyle}>Justificativa *</label>
                    <textarea
                        style={{ ...inputStyle, minHeight: '64px', resize: 'vertical' }}
                        value={justification}
                        onChange={e => setJustification(e.target.value)}
                        placeholder="Por que este documento é classificado assim (ex.: Factura Pró-forma FT… anexada ao pedido original)."
                    />
                    {fieldError('Justification')}
                    {justificationTooShort && justification.length > 0 && (
                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, marginTop: '2px' }}>
                            A justificativa deve ter pelo menos {RECONCILIATION_JUSTIFICATION_MIN_LENGTH} caracteres.
                        </div>
                    )}
                </div>

                {error && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: '#b91c1c', fontSize: '0.85rem', fontWeight: 700 }}>
                        <AlertTriangle size={15} /> {error}
                    </div>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '4px' }}>
                    <button onClick={onClose} disabled={saving} style={{
                        padding: '9px 16px', border: '1px solid var(--color-border)', backgroundColor: '#fff',
                        borderRadius: '8px', fontWeight: 700, cursor: 'pointer'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={() => void submit()} disabled={!canSubmit} style={{
                        padding: '9px 18px', border: 'none', backgroundColor: 'var(--color-primary)',
                        color: '#fff', borderRadius: '8px', fontWeight: 800, cursor: canSubmit ? 'pointer' : 'not-allowed',
                        opacity: canSubmit ? 1 : 0.6
                    }}>
                        {saving ? 'A classificar…' : 'Classificar Documento de Origem'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
