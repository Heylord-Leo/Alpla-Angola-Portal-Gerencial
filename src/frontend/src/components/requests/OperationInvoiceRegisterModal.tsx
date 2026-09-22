import { useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, Upload, FileCheck2 } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { MoneyInput } from '../ui/MoneyInput';
import { api, ApiError } from '../../lib/api';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { mapOperationInvoiceError, formatMoney, formatUtcTimestampDate } from '../../lib/operationInvoiceView';
import { createInvoiceSubmitter, releasePreviousUpload, pickResumableAttachment, sortRecoverableCandidates } from '../../lib/operationInvoiceSubmit';
import type {
    OperationInvoiceDto,
    OperationInvoiceDuplicateResultDto,
    OperationInvoiceUnclaimedAttachmentDto,
    SaveOperationInvoiceDto
} from '../../types/operationInvoice';

export type RegisterModalMode = 'create' | 'edit' | 'replace';

interface OperationInvoiceRegisterModalProps {
    requestId: string;
    mode: RegisterModalMode;
    /** The invoice being edited, or the VALIDATED original being replaced. Null on create. */
    invoice: OperationInvoiceDto | null;
    /**
     * v2.228.4: the ONLY valid invoice suppliers — the request's obligation-bearing group
     * suppliers. Never the global supplier catalogue; the backend enforces the same rule
     * (OPERATION_INVOICE_SUPPLIER_NOT_IN_REQUEST).
     */
    supplierOptions: { id: number; name: string }[];
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
    requestId, mode, invoice, supplierOptions, onClose, onSaved
}: OperationInvoiceRegisterModalProps) {
    const [form, setForm] = useState<FormState>(() => ({
        // Single obligation supplier → preselected; several → user picks among them.
        supplierId: invoice?.supplierId
            ?? (supplierOptions.length === 1 ? supplierOptions[0].id : null),
        supplierName: invoice?.supplierName
            ?? (supplierOptions.length === 1 ? supplierOptions[0].name : ''),
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

    // v2.245.8: upload-order safety. The submitter serializes attempts (double-clicks are refused, not
    // queued). The uploaded file is a SERVER fact: an attempt whose create failed leaves an unclaimed
    // OPERATION_INVOICE attachment the backend lists (`unclaimed-attachments`), and the create is
    // idempotent per attachment, so a retry never uploads a second file nor creates a second invoice.
    //   • the upload of THIS uninterrupted session is restored automatically — only while the server
    //     still lists it as unclaimed;
    //   • uploads merely DISCOVERED on the server (closed modal, reload, another session) are listed as
    //     candidates (file, date/time, uploader) and NEVER selected automatically: the user chooses
    //     "Reutilizar" or "Descartar" per file; until then no candidate satisfies the file requirement;
    //   • choosing a new local file while an upload is selected EXPLICITLY releases it on the server
    //     (refused when an invoice claims it); every release/claim answer refreshes the server list.
    const submitRef = useRef(createInvoiceSubmitter());
    const retainedAttachmentRef = useRef<string | null>(null);
    const [retainedNotice, setRetainedNotice] = useState<string | null>(null);
    /** The upload the submission will claim (restored own upload, or an explicitly reused candidate). */
    const [selectedUpload, setSelectedUpload] = useState<OperationInvoiceUnclaimedAttachmentDto | null>(null);
    /** Server-discovered unclaimed uploads awaiting an explicit decision (deterministic order). */
    const [candidates, setCandidates] = useState<OperationInvoiceUnclaimedAttachmentDto[]>([]);
    const [recoveryNotice, setRecoveryNotice] = useState<string | null>(null);

    /** Re-reads server truth; restores ONLY this session's own upload, never a discovered one. */
    const refreshCandidates = async (): Promise<void> => {
        if (mode !== 'create') return;
        try {
            const list = sortRecoverableCandidates(await operationInvoiceApi.listUnclaimedAttachments(requestId));
            const own = pickResumableAttachment(list, retainedAttachmentRef.current);
            retainedAttachmentRef.current = own?.attachmentId ?? null;
            setSelectedUpload(own);
            setCandidates(list.filter(c => c.attachmentId !== own?.attachmentId));
            setRetainedNotice(own
                ? `Ficheiro já carregado nesta sessão: ${own.fileName} (${formatUtcTimestampDate(own.uploadedAtUtc)}). Será reutilizado — não é carregado novamente.`
                : null);
        } catch {
            /* recovery is best-effort; a fresh upload path remains available */
        }
    };

    useEffect(() => {
        if (mode !== 'create') return;
        let cancelled = false;
        (async () => { if (!cancelled) await refreshCandidates(); })();
        return () => { cancelled = true; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [requestId, mode]);

    const isClaimedError = (err: unknown) => mapOperationInvoiceError(err).code === 'OPERATION_INVOICE_ATTACHMENT_CLAIMED';

    /** Explicit "Reutilizar": the discovered upload becomes the selected one for this submission. */
    const reuseCandidate = (candidate: OperationInvoiceUnclaimedAttachmentDto) => {
        retainedAttachmentRef.current = candidate.attachmentId;
        setSelectedUpload(candidate);
        setCandidates(prev => prev.filter(c => c.attachmentId !== candidate.attachmentId));
        setFile(null);
        setRecoveryNotice(null);
        setRetainedNotice(`Ficheiro reutilizado: ${candidate.fileName} (${formatUtcTimestampDate(candidate.uploadedAtUtc)}). Não é carregado novamente.`);
    };

    /**
     * Explicit server-side release of ONE unclaimed upload (the selected one, or a discovered candidate).
     * Every outcome refreshes server truth; a claimed answer names the existing invoice; a failure never
     * makes the client forget the upload — it stays listed until it is resolved.
     */
    const releaseUpload = async (attachmentId: string): Promise<'released' | 'claimed' | 'failed'> => {
        const wasSelected = retainedAttachmentRef.current === attachmentId;
        const previousSelection = wasSelected ? selectedUpload : null;
        if (wasSelected) {
            // Supersede locally FIRST (synchronously): a submit clicked while the round-trip is in flight
            // must never claim an upload the user just chose to discard.
            retainedAttachmentRef.current = null;
            setSelectedUpload(null);
            setRetainedNotice(null);
        }
        const outcome = await releasePreviousUpload(
            (id) => operationInvoiceApi.releaseUnclaimedAttachment(requestId, id), attachmentId, isClaimedError);
        if (outcome === 'released') {
            setRecoveryNotice(null);
        } else if (outcome === 'claimed') {
            setRecoveryNotice('Esse ficheiro já está registado como fatura final — a lista de faturas foi atualizada.');
        } else {
            // Unresolved: the upload is NOT forgotten — it stays selected (server truth re-read below
            // decides whether it still exists) and the user must resolve it explicitly.
            setRecoveryNotice('Não foi possível descartar o ficheiro; continua listado até ser resolvido.');
            if (wasSelected && previousSelection) {
                retainedAttachmentRef.current = attachmentId;
                setSelectedUpload(previousSelection);
            }
        }
        await refreshCandidates();
        return outcome;
    };

    /** A new local file supersedes the selected upload only through an explicit, successful release. */
    const handleLocalFileChosen = async (chosen: File | null) => {
        const selected = retainedAttachmentRef.current;
        if (selected) {
            const outcome = await releaseUpload(selected);
            if (outcome === 'failed') {
                // Unresolved upload: it stays selected/listed and the new local file is refused.
                setFile(null);
                setError('Resolva primeiro o ficheiro já carregado (Descartar ou Reutilizar) antes de escolher outro.');
                return;
            }
        }
        setError(null);
        setFile(chosen);
    };

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

        if (needsNewFile && !file && mode === 'create' && !retainedAttachmentRef.current) {
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

            const buildPayload = (attachmentId: string | null): SaveOperationInvoiceDto => ({
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
            });

            // The Portal's one upload mechanism (distinct Final Invoice context) — called ONLY after the
            // backend preflight accepted the registration, and only when no retained upload exists.
            const uploadEvidence = async (): Promise<string> => {
                const uploaded = await api.attachments.upload(requestId, [file!], 'OPERATION_INVOICE');
                const id = Array.isArray(uploaded) && uploaded[0]?.id ? uploaded[0].id : null;
                if (!id) throw new Error('O carregamento do anexo não devolveu um identificador válido.');
                return id;
            };

            if (mode === 'edit' && invoice) {
                // Header edit: no admissibility preflight exists for updates; a new file is optional.
                let attachmentId = invoice.attachmentId ?? null;
                if (file) attachmentId = await uploadEvidence();
                await operationInvoiceApi.update(requestId, invoice.id, buildPayload(attachmentId));
                onSaved();
                return;
            }

            const outcome = await submitRef.current({
                preflight: mode === 'create'
                    ? () => operationInvoiceApi.preflightCreate(requestId)
                    : async () => undefined,   // replace has no create preflight; retention still protects it
                upload: uploadEvidence,
                create: (attachmentId) => mode === 'create'
                    ? operationInvoiceApi.create(requestId, buildPayload(attachmentId))
                    : operationInvoiceApi.replace(requestId, invoice!.id, {
                        ...buildPayload(attachmentId),
                        replacementReason: replacementReason.trim()
                    })
            }, { retainedAttachmentId: retainedAttachmentRef.current });

            if (outcome.ok) {
                retainedAttachmentRef.current = null;
                setSelectedUpload(null);
                onSaved();
                return;
            }
            if (outcome.stage === 'busy') return;   // a submission is already in flight
            if (outcome.stage === 'create') {
                // The file is in the Portal (server-listed as unclaimed): this session restores it
                // automatically (server-confirmed); a lost successful response resolves to the same
                // invoice on retry (create is idempotent per attachment). After a close/reload it is
                // offered as a candidate for an explicit decision instead.
                retainedAttachmentRef.current = outcome.attachmentId;
                await refreshCandidates();
            }
            throw outcome.error;
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
                    <select
                        style={inputStyle}
                        value={form.supplierId ?? ''}
                        onChange={e => {
                            const id = e.target.value ? Number(e.target.value) : null;
                            const option = supplierOptions.find(o => o.id === id);
                            set({ supplierId: id, supplierName: option?.name ?? '' });
                        }}
                    >
                        <option value="">Selecionar fornecedor…</option>
                        {supplierOptions.map(o => (
                            <option key={o.id} value={o.id}>{o.name}</option>
                        ))}
                        {/* Edit/replace of an invoice whose supplier predates the rule: keep it
                            visible so the header renders honestly; the backend decides. */}
                        {form.supplierId != null && !supplierOptions.some(o => o.id === form.supplierId) && (
                            <option value={form.supplierId}>{form.supplierName || `Fornecedor ${form.supplierId}`}</option>
                        )}
                    </select>
                    <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600, marginTop: '2px' }}>
                        Apenas fornecedores com obrigação de Fatura Final neste pedido.
                    </div>
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
                        <MoneyInput style={inputStyle} value={form.netAmount}
                                    onChange={v => set({ netAmount: v })} />
                        {fieldError('NetAmount')}
                    </div>
                    <div>
                        <label style={labelStyle}>Imposto</label>
                        <MoneyInput style={inputStyle} value={form.taxAmount}
                                    onChange={v => set({ taxAmount: v })} />
                    </div>
                    <div>
                        <label style={labelStyle}>Total (bruto) *</label>
                        <MoneyInput style={inputStyle} value={form.grossAmount}
                                    onChange={v => set({ grossAmount: v })} />
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
                        {file || selectedUpload ? <FileCheck2 size={16} /> : <Upload size={16} />}
                        {file ? file.name
                            : selectedUpload ? `Reutilizar: ${selectedUpload.fileName}`
                            : mode === 'edit' && invoice?.attachmentFileName
                                ? `Atual: ${invoice.attachmentFileName}`
                                : 'Selecionar o PDF/imagem da fatura final'}
                        <input type="file" accept=".pdf,.png,.jpg,.jpeg" style={{ display: 'none' }}
                               onChange={e => {
                                   // A different file is a different registration: the selected upload is
                                   // released on the server first (never merely forgotten).
                                   void handleLocalFileChosen(e.target.files?.[0] ?? null);
                               }} />
                    </label>
                    {fieldError('AttachmentId')}
                    {retainedNotice && selectedUpload && (
                        <div data-testid="selected-upload" style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, marginTop: '4px', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
                            <span>{retainedNotice}</span>
                            <button type="button" onClick={() => void releaseUpload(selectedUpload.attachmentId)} disabled={saving} style={{
                                border: '1px solid var(--color-border)', backgroundColor: '#fff', borderRadius: '6px',
                                padding: '2px 8px', fontWeight: 700, cursor: 'pointer', fontSize: '0.72rem'
                            }}>
                                Descartar ficheiro carregado
                            </button>
                        </div>
                    )}
                    {candidates.length > 0 && (
                        <div data-testid="recoverable-uploads" style={{
                            marginTop: '8px', padding: '10px 12px', backgroundColor: '#fffbeb', border: '1px solid #fde68a',
                            borderRadius: '8px', display: 'flex', flexDirection: 'column', gap: '6px', fontSize: '0.78rem', color: '#92400e'
                        }}>
                            <span style={{ fontWeight: 800 }}>
                                Ficheiro{candidates.length > 1 ? 's' : ''} de fatura final já carregado{candidates.length > 1 ? 's' : ''} sem fatura registada
                            </span>
                            <span style={{ fontWeight: 600 }}>
                                Nenhum é usado automaticamente. Escolha <b>Reutilizar</b> para registar a fatura com esse ficheiro
                                ou <b>Descartar</b> para o remover; um ficheiro não decidido nunca é submetido.
                            </span>
                            {candidates.map(c => (
                                <div key={c.attachmentId} style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                    <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>{c.fileName}</span>
                                    <span>{formatUtcTimestampDate(c.uploadedAtUtc)}{c.uploadedByName ? ` · ${c.uploadedByName}` : ''}</span>
                                    <button type="button" onClick={() => reuseCandidate(c)} disabled={saving} style={{
                                        border: 'none', backgroundColor: 'var(--color-primary)', color: '#fff', borderRadius: '6px',
                                        padding: '2px 8px', fontWeight: 700, cursor: 'pointer', fontSize: '0.72rem'
                                    }}>
                                        Reutilizar
                                    </button>
                                    <button type="button" onClick={() => void releaseUpload(c.attachmentId)} disabled={saving} style={{
                                        border: '1px solid var(--color-border)', backgroundColor: '#fff', borderRadius: '6px',
                                        padding: '2px 8px', fontWeight: 700, cursor: 'pointer', fontSize: '0.72rem'
                                    }}>
                                        Descartar
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}
                    {recoveryNotice && (
                        <div style={{ fontSize: '0.75rem', color: '#b45309', fontWeight: 600, marginTop: '4px' }}>{recoveryNotice}</div>
                    )}
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
