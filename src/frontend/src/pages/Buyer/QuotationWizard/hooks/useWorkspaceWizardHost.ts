import { useEffect, useRef, useState } from 'react';
import { api } from '../../../../lib/api';
import { AmbiguousSavePreAttemptSnapshot, IvaRate, SavedQuotationDto, Unit } from '../../../../types';
import { quotationEditMode } from '../resolveContributingQuotations';
import { computeFileHash } from '../../../../lib/utils';
import { useOcrProcessor } from '../../../../hooks/useOcrProcessor';
import { useQuotationWizardState, QuotationWizardSource } from './useQuotationWizardState';
import { createBuyerQuotationWizardController, WizardFeedback } from '../buyerQuotationWizardController';
import { toWizardActiveRequest } from '../workspaceWizardRequest';

// ─────────────────────────────────────────────────────────────────────────────
// Buyer Workspace wizard host (Phase 3C.1 / Stage 2B-R). Workspace-LOCAL glue: it owns the same host
// STATE shapes the classic screen owns (wizard state, active request, saving/OCR flags, temp-attachment
// ids, ambiguous-save ref) and feeds them to the SHARED, already-accepted
// `createBuyerQuotationWizardController` — the handler bodies are NOT duplicated. Per the corrected
// plan, state ownership is intentionally per-host (classic keeps its own; the Workspace keeps its own);
// only the orchestration handlers are single-source. Wizard internals are untouched.
//
// Duplicate-file protection is preserved WITHOUT copying the classic 60-line modal/countdown: the same
// api.attachments.checkDuplicate decision runs here, surfaced through a small `dupWarning` object the
// Workspace renders as a standard confirmation dialog.
// ─────────────────────────────────────────────────────────────────────────────

export interface WorkspaceDupWarning {
    file: File;
    fileName: string;
    requestNumber?: string;
    uploadedBy?: string;
    createdAtUtc?: string;
}

export function useWorkspaceWizardHost(opts: {
    onSaved: () => void | Promise<void>;
    onFeedback: (f: WizardFeedback) => void;
}) {
    const [ivaRates, setIvaRates] = useState<IvaRate[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [currencies, setCurrencies] = useState<any[]>([]);
    useEffect(() => {
        Promise.all([
            api.lookups.getIvaRates(true).catch(() => []),
            api.lookups.getUnits(true).catch(() => []),
            api.lookups.getCurrencies(true).catch(() => []),
        ]).then(([iva, u, c]) => { setIvaRates(iva); setUnits(u); setCurrencies(c); });
    }, []);

    const { mapOcrResultToDraft } = useOcrProcessor(ivaRates, units, currencies);

    // Workspace-local host state (same shapes as classic).
    const quotationWizardState = useQuotationWizardState();
    const [wizardActiveRequest, setWizardActiveRequest] = useState<any | null>(null);
    const [temporaryWizardAttachmentIds, setTemporaryWizardAttachmentIds] = useState<string[]>([]);
    const [isSaving, setIsSaving] = useState(false);
    const [isProcessingOcr, setIsProcessingOcr] = useState<Record<string, boolean>>({});
    const preAttemptSnapshotRef = useRef<AmbiguousSavePreAttemptSnapshot | 'unavailable' | null>(null);
    const [dupWarning, setDupWarning] = useState<WorkspaceDupWarning | null>(null);

    const controller = createBuyerQuotationWizardController({
        quotationWizardState,
        wizardActiveRequest,
        setWizardActiveRequest,
        setIsSaving,
        setIsProcessingOcr,
        temporaryWizardAttachmentIds,
        setTemporaryWizardAttachmentIds,
        preAttemptSnapshotRef,
        mapOcrResultToDraft,
        onSaved: opts.onSaved,
        onFeedback: opts.onFeedback,
    });

    /**
     * Load the canonical RequestDetailsDto (same shape classic uses) and open the wizard directly in the
     * EXPLICIT entry method chosen by the Workspace. `mode` is the existing canonical Wizard source
     * (`'UPLOAD'` → document/OCR flow, `'MANUAL'` → priceable-rows flow) — NOT a new parallel enum, and
     * NOT a boolean. Eligibility/context are identical for both modes (`api.requests.get`); only HOW the
     * quotation data is entered differs. Defaults to MANUAL for safety if a caller omits the method.
     */
    const openAddQuotation = async (requestId: string, mode: QuotationWizardSource = 'MANUAL') => {
        try {
            const request = await api.requests.get(requestId);
            // RequestDetailsDto exposes the request GUID as `id`, not `requestId`. Stamp the wizard's
            // `requestId` contract from the GUID we opened for, so upload/OCR/save don't hit `/…/undefined`.
            controller.handleOpenWizard(toWizardActiveRequest(request, requestId), mode);
        } catch (e: any) {
            opts.onFeedback({ type: 'error', message: e?.message || 'Falha ao abrir o assistente de cotação.' });
        }
    };

    /**
     * Phase 4 — open a REVISION of an existing contributing quotation for a Buyer commercial correction
     * (the rework "Gerenciar Cotações" bridge). The wizard is SEEDED from the original quotation (item
     * rows/prices/supplier/document/mappings) but persists as a NEW quotation identity — the original and
     * its frozen candidate stay immutable. `reworkBatchId` is forwarded so the backend applies the narrow
     * rework status exception. Mode follows the quotation's own source (`quotationEditMode`).
     */
    const openReviseQuotation = async (requestId: string, quotation: SavedQuotationDto, reworkBatchId: string) => {
        try {
            const request = await api.requests.get(requestId);
            controller.handleOpenWizard(toWizardActiveRequest(request, requestId), quotationEditMode(quotation), quotation, reworkBatchId);
        } catch (e: any) {
            opts.onFeedback({ type: 'error', message: e?.message || 'Falha ao abrir o assistente de cotação.' });
        }
    };

    // Exact-file duplicate check BEFORE upload (same decision as classic; small presentation here).
    const onUploadFile = async (file: File) => {
        try {
            const hash = await computeFileHash(file);
            const dup = await api.attachments.checkDuplicate(hash);
            if (dup.isDuplicate) {
                setDupWarning({ file, fileName: file.name, requestNumber: dup.requestNumber, uploadedBy: dup.uploadedBy, createdAtUtc: dup.createdAtUtc });
                return;
            }
        } catch {
            // Non-blocking: a failed dup check must not block the upload (mirrors classic).
        }
        controller.startWizardUpload(file);
    };
    const confirmDupUpload = () => { const w = dupWarning; setDupWarning(null); if (w) controller.startWizardUpload(w.file); };
    const dismissDup = () => setDupWarning(null);

    return {
        wizardState: quotationWizardState,
        wizardActiveRequest,
        isProcessingOcr,
        isSaving,
        controller,
        ivaRates, units, currencies,
        openAddQuotation,
        openReviseQuotation,
        onUploadFile,
        dupWarning, confirmDupUpload, dismissDup,
    };
}
