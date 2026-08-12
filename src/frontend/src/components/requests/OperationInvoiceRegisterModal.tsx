import { useMemo, useState } from 'react';
import { AlertTriangle, Upload, FileCheck2 } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { api, ApiError } from '../../lib/api';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { mapOperationInvoiceError, formatMoney } from '../../lib/operationInvoiceView';
import { SupplierAutocomplete } from '../SupplierAutocomplete';
import type {
    OperationInvoiceDto,
    OperationInvoiceDuplicateResultDto,
    SaveOperationInvoiceDto
} from '../../types/operationInvoice';

export type RegisterModalMode = 'create' | 'edit' | 'replace';

interface OperationInvoiceRegisterModalProps {
    requestId: string;
    mode: RegisterModalMode;
    /** The invoice being edited, or the VALIDATED original being replaced. Null on create. */
    invoice: OperationInvoiceDto | null;
    onClose: () => void;
    onSaved: () => void;
}

interface FormState {
    supplierId: number | null;
    supplierName: string;
    documentNumber: string;
    documentSeries: string;
    documentDate: string;
    dueDate: string;
    currency: string;
    netAmount: string;
    taxAmount: string;
    grossAmount: string;
    notes: string;
}

/**
 * Release 4 Phase 3B — "Registrar Fatura Final".
 *
 * Manual registration of the operation invoice (the final FISCAL invoice — never a Cotação, never
 * a Proforma). The document evidence is uploaded as a distinct TYPE_OPERATION_INVOICE attachment;
 * unrelated request attachments are never reclassified. Registration is manual by definition in
 * Phase 3B (OCR is Phase 5), so AmountsEnteredManually rides as true.
 *
 * Duplicate preflight is advisory: the backend Create stays authoritative against races.
 */
export function OperationInvoiceRegisterModal({
    requestId, mode, invoice, onClose, onSaved
}: OperationInvoiceRegisterModalProps) {
    const [form, setForm] = useState<FormState>(() => ({
        supplierId: invoice?.supplierId ?? null,
        supplierName: invoice?.supplierName ?? '',
        documentNumber: mode === 'replace' ? (invoice?.documentNumber ?? '') : (invoice?.documentNumber ?? ''),
        documentSeries: invoice?.documentSeries ?? '',
        documentDate: invoice?.documentDate ? invoice.documentDate.substring(0, 10) : '',
        dueDate: invoice?.dueDate ? invoice.dueDate.substring(0, 10) : '',
        currency: invoice?.currency ?? 'AOA',
        netAmount: mode === 'replace' ? '' : invoice?.netAmount != null ? String(invoice.netAmount) : '',
        taxAmount: mode === 'replace' ? '' : invoice?.taxAmount != null ? String(invoice.taxAmount) : '',
        grossAmount: mode === 'replace' ? '' : invoice?.grossAmount != null ? String(invoice.grossAmount) : '',
        notes: invoice?.notes ?? ''
    }));
    const [file, setFile] = useState<File | null>(null);
    const [replacementReason, setReplacementReason] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const [duplicateInfo, setDuplicateInfo] = useState<OperationInvoiceDuplicateResultDto | null>(null);
    const [duplicateAcknowledged, setDuplicateAcknowledged] = useState(false);

    const title = mode === 'create' ? 'Registrar Fatura Final'
        : mode === 'edit' ? 'Editar Fatura Final'
        : 'Substituir Fatura Validada';

    const needsNewFile = mode !== 'edit';

    const numbers = useMemo(() => {
        const parse = (v: string) => v.trim() === '' ? null : Number(v.replace(',', '.'));
        return {
            net: parse(form.netAmount),
            tax: parse(form.taxAmount),
            gross: parse(form.grossAmount)
        };
    }, [form.netAmount, form.taxAmount, form.grossAmount]);

    const netTaxMismatch = numbers.net != null && numbers.tax != null && numbers.gross != null &&
        Math.abs(numbers.gross - (numbers.net + numbers.tax)) > Math.max(1, Math.abs(numbers.gross) * 0.001);

    const set = (patch: Partial<FormState>) => {
        setForm(f => ({ ...f, ...patch }));
        setDuplicateInfo(null);
        setDuplicateAcknowledged(false);
    };

    const handleSubmit = async () => {
        setError(null);
        setFieldErrors({});

        if (needsNewFile && !file && mode === 'create') {
            setError('Anexe o ficheiro da fatura final antes de registar.');
            return;
        }
        if (mode === 'replace' && (!file || replacementReason.trim().length === 0)) {
            setError('A substituição exige o ficheiro corrigido e o motivo da substituição.');
            return;
        }

        setSaving(true);
        try {
            // ── Advisory duplicate preflight (fiscal identity). Once acknowledged, the user may
            // proceed — the backend remains the enforcement and will still refuse a true duplicate.
            if (mode === 'create' && !duplicateAcknowledged && form.supplierId && form.documentNumber.trim()) {
                const preflight = await operationInvoiceApi.checkDuplicate(requestId, {
                    supplierId: form.supplierId,
                    documentNumber: form.documentNumber.trim(),
                    documentSeries: form.documentSeries.trim() || null
                });
                if (preflight.hasDuplicate) {
                    setDuplicateInfo(preflight);
                    setDuplicateAcknowledged(true);   // second click proceeds to the authoritative check
                    setSaving(false);
                    return;
                }
            }

            // ── Attachment upload (distinct Final Invoice context) ──
            let attachmentId = invoice?.attachmentId ?? null;
            if (file) {
                const uploaded = await api.attachments.upload(requestId, [file], 'OPERATION_INVOICE');
                attachmentId = Array.isArray(uploaded) && uploaded[0]?.id ? uploaded[0].id : attachmentId;
                if (!attachmentId) {
                    setError('O carregamento do anexo não devolveu um identificador válido.');
                    setSaving(false);
                    return;
                }
            }

            const payload: SaveOperationInvoiceDto = {
                attachmentId,
                supplierId: form.supplierId,
                documentNumber: form.documentNumber.trim() || null,
                documentSeries: form.documentSeries.trim() || null,
                documentDate: form.documentDate || null,
                dueDate: form.dueDate || null,
                currency: form.currency.trim().toUpperCase() || null,
                netAmount: numbers.net,
                taxAmount: numbers.tax,
                grossAmount: numbers.gross,
                notes: form.notes.trim() || null,
                amountsEnteredManually: true,
                rowVersion: mode === 'edit' || mode === 'replace' ? invoice?.rowVersion ?? null : null
            };

            if (mode === 'create') {
                await operationInvoiceApi.create(requestId, payload);
            } else if (mode === 'edit' && invoice) {
                await operationInvoiceApi.update(requestId, invoice.id, payload);
            } else if (mode === 'replace' && invoice) {
                await operationInvoiceApi.replace(requestId, invoice.id, {
                    ...payload,
                    replacementReason: replacementReason.trim()
                });
            }
            onSaved();
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

    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
        borderRadius: '8px', fontSize: '0.88rem', boxSizing: 'border-box'
    };
    const labelStyle: React.CSSProperties = {
        fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)',
        textTransform: 'uppercase', marginBottom: '4px', display: 'block'
    };
    const fieldError = (key: string) => fieldErrors[key]?.length
        ? <div style={{ color: '#b91c1c', fontSize: '0.75rem', fontWeight: 600, marginTop: '2px' }}>{fieldErrors[key][0]}</div>
        : null;

    return (
        <ModalWrapper title={title} onClose={onClose} width={620}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                {mode === 'replace' && invoice && (
                    <div style={{ fontSize: '0.82rem', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px', padding: '10px 12px', color: '#1e40af', fontWeight: 600 }}>
                        A fatura validada {invoice.documentNumber} será marcada como substituída e a
                        fatura corrigida entra em validação como qualquer outra. Nada é transferido
                        automaticamente — distribuições são feitas de novo na fatura corrigida.
                    </div>
                )}

                <div>
                    <label style={labelStyle}>Fornecedor *</label>
                    <SupplierAutocomplete
                        initialName={form.supplierName}
                        excludeInternal
                        onChange={(id, name) => set({ supplierId: id, supplierName: name })}
                    />
                    {fieldError('SupplierId')}
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '10px' }}>
                    <div>
                        <label style={labelStyle}>Número da fatura *</label>
                        <input style={inputStyle} value={form.documentNumber}
                               onChange={e => set({ documentNumber: e.target.value })} />
                        {fieldError('DocumentNumber')}
                    </div>
                    <div>
                        <label style={labelStyle}>Série</label>
                        <input style={inputStyle} value={form.documentSeries}
                               onChange={e => set({ documentSeries: e.target.value })} />
                    </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '10px' }}>
                    <div>
                        <label style={labelStyle}>Data do documento *</label>
                        <input type="date" style={inputStyle} value={form.documentDate}
                               onChange={e => set({ documentDate: e.target.value })} />
                        {fieldError('DocumentDate')}
                    </div>
                    <div>
                        <label style={labelStyle}>Vencimento</label>
                        <input type="date" style={inputStyle} value={form.dueDate}
                               onChange={e => set({ dueDate: e.target.value })} />
                    </div>
                    <div>
                        <label style={labelStyle}>Moeda *</label>
                        <input style={inputStyle} value={form.currency} maxLength={3}
                               onChange={e => set({ currency: e.target.value.toUpperCase() })} />
                        {fieldError('Currency')}
                    </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '10px' }}>
                    <div>
                        <label style={labelStyle}>Valor líquido</label>
                        <input type="number" step="0.01" style={inputStyle} value={form.netAmount}
                               onChange={e => set({ netAmount: e.target.value })} />
                        {fieldError('NetAmount')}
                    </div>
                    <div>
                        <label style={labelStyle}>Imposto</label>
                        <input type="number" step="0.01" style={inputStyle} value={form.taxAmount}
                               onChange={e => set({ taxAmount: e.target.value })} />
                    </div>
                    <div>
                        <label style={labelStyle}>Total (bruto) *</label>
                        <input type="number" step="0.01" style={inputStyle} value={form.grossAmount}
                               onChange={e => set({ grossAmount: e.target.value })} />
                        {fieldError('GrossAmount')}
                    </div>
                </div>

                {netTaxMismatch && (
                    <div style={{ fontSize: '0.78rem', color: '#b45309', fontWeight: 600 }}>
                        Líquido + imposto não corresponde ao total — verifique os valores antes de guardar.
                    </div>
                )}

                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                    Valores informados manualmente (a leitura automática chega numa fase posterior).
                </div>

                <div>
                    <label style={labelStyle}>Notas</label>
                    <textarea style={{ ...inputStyle, minHeight: '56px', resize: 'vertical' }} value={form.notes}
                              onChange={e => set({ notes: e.target.value })} />
                </div>

                {mode === 'replace' && (
                    <div>
                        <label style={labelStyle}>Motivo da substituição *</label>
                        <textarea style={{ ...inputStyle, minHeight: '56px', resize: 'vertical' }}
                                  value={replacementReason}
                                  onChange={e => setReplacementReason(e.target.value)}
                                  placeholder="Por que a fatura validada está a ser substituída?" />
                    </div>
                )}

                {/* ── Evidence file ── */}
                <div>
                    <label style={labelStyle}>
                        {mode === 'edit' ? 'Ficheiro da fatura (substituir apenas se necessário)' : 'Ficheiro da fatura *'}
                    </label>
                    <label style={{
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 12px',
                        border: '1px dashed var(--color-border)', borderRadius: '8px', cursor: 'pointer',
                        fontSize: '0.85rem', fontWeight: 600,
                        color: file ? '#15803d' : 'var(--color-text-muted)'
                    }}>
                        {file ? <FileCheck2 size={16} /> : <Upload size={16} />}
                        {file ? file.name : mode === 'edit' && invoice?.attachmentFileName
                            ? `Atual: ${invoice.attachmentFileName}`
                            : 'Selecionar o PDF/imagem da fatura final'}
                        <input type="file" accept=".pdf,.png,.jpg,.jpeg" style={{ display: 'none' }}
                               onChange={e => setFile(e.target.files?.[0] ?? null)} />
                    </label>
                    {fieldError('AttachmentId')}
                </div>

                {duplicateInfo?.hasDuplicate && (
                    <div style={{
                        display: 'flex', flexDirection: 'column', gap: '6px', padding: '10px 12px',
                        backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '8px',
                        fontSize: '0.82rem', color: '#92400e', fontWeight: 600
                    }}>
                        <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <AlertTriangle size={15} /> Possível duplicado detetado:
                        </span>
                        {duplicateInfo.sameBusinessDocument && (
                            <span>
                                Já existe a fatura {duplicateInfo.sameBusinessDocument.documentNumber}
                                {duplicateInfo.sameBusinessDocument.documentSeries ? ` (série ${duplicateInfo.sameBusinessDocument.documentSeries})` : ''}
                                {duplicateInfo.sameBusinessDocument.requestNumber ? ` no pedido ${duplicateInfo.sameBusinessDocument.requestNumber}` : ''}.
                            </span>
                        )}
                        {duplicateInfo.sameFile && (
                            <span>
                                Este ficheiro já corresponde a uma fatura registada
                                {duplicateInfo.sameFile.requestNumber ? ` no pedido ${duplicateInfo.sameFile.requestNumber}` : ''}.
                            </span>
                        )}
                        <span>Confirme antes de prosseguir — o registo será novamente verificado pelo servidor.</span>
                    </div>
                )}

                {error && (
                    <div style={{ color: '#b91c1c', fontSize: '0.85rem', fontWeight: 700 }}>{error}</div>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '4px' }}>
                    <button onClick={onClose} disabled={saving} style={{
                        padding: '9px 16px', border: '1px solid var(--color-border)', backgroundColor: '#fff',
                        borderRadius: '8px', fontWeight: 700, cursor: 'pointer'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={() => void handleSubmit()} disabled={saving} style={{
                        padding: '9px 18px', border: 'none', backgroundColor: 'var(--color-primary)',
                        color: '#fff', borderRadius: '8px', fontWeight: 800, cursor: 'pointer',
                        opacity: saving ? 0.7 : 1
                    }}>
                        {saving ? 'A guardar…'
                            : duplicateInfo?.hasDuplicate ? 'Prosseguir mesmo assim'
                            : mode === 'replace' ? 'Substituir Fatura'
                            : mode === 'edit' ? 'Guardar Alterações'
                            : 'Registrar Fatura Final'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
