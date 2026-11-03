import { describe, it, expect } from 'vitest';
import correctPoSrc from './CorrectPoModal.tsx?raw';
import registerPoSrc from './RegisterPoModal.tsx?raw';
import attachmentsSrc from './RequestAttachments.tsx?raw';
import quotationsSrc from '../pages/Requests/components/RequestQuotations.tsx?raw';
import buyerItemsSrc from '../pages/Buyer/BuyerItemsList.tsx?raw';
import approvalPanelSrc from '../pages/Approvals/ApprovalDetailPanel.tsx?raw';
import printModelSrc from '../pages/Requests/components/print/requestPrintModel.ts?raw';
import wizardAwardSrc from '../pages/Approvals/WizardStepAward.tsx?raw';

/**
 * v2.249.5 — source-level wiring (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md): the correction wizard no longer
 * deletes the current P.O., both wizards clean up only their temporary upload (clear file + cancel) and never after a
 * successful registration, the attachment list never offers the deletion of registered evidence, and the "Menor valor" /
 * lifecycle surfaces read the shared helpers instead of comparing totals across documents.
 */
describe('CorrectPoModal — registered P.O. evidence is never removed', () => {
    it('has no "Remover P.O atual" action and no attachment delete of the existing P.O.', () => {
        expect(correctPoSrc).not.toContain('Remover P.O atual');
        expect(correctPoSrc).not.toContain('handleRemovePo');
        expect(correctPoSrc).not.toMatch(/api\.attachments\.delete\(existingPo/);
        expect(correctPoSrc).not.toContain('poRemoved');
        expect(correctPoSrc).not.toMatch(/Trash2/);
    });
    it('tells the operator the current P.O. becomes history (superseded), not removed', () => {
        expect(correctPoSrc).toContain('ficará como histórico (substituída) após a correção; não é removida');
    });
    it('cleans up the temporary upload on clear-file and on cancel, through the shared helper, and clears the ref on success', () => {
        expect(correctPoSrc).toMatch(/import \{ cleanupTemporaryPoUpload \} from '\.\.\/lib\/poEvidence';/);
        const clear = correctPoSrc.slice(correctPoSrc.indexOf('const handleClearFile = () => {'), correctPoSrc.indexOf('const handleCancel = () => {'));
        expect(clear).toMatch(/cleanupTemporaryPoUpload\(api\.attachments\.delete, uploadedAttachmentIdRef\.current, reportCleanupFailure, reportCleanupKept\)/);
        const cancel = correctPoSrc.slice(correctPoSrc.indexOf('const handleCancel = () => {'), correctPoSrc.indexOf('const handleConfirm = async () => {'));
        expect(cancel).toMatch(/if \(processing\) return;/);
        expect(cancel).toMatch(/cleanupTemporaryPoUpload\(api\.attachments\.delete, uploadedAttachmentIdRef\.current, reportCleanupFailure, reportCleanupKept\)/);
        expect(cancel).toMatch(/onClose\(\);/);
        expect(correctPoSrc).toMatch(/onClick=\{handleCancel\}\s*\n\s*disabled=\{processing\}/);
        expect(correctPoSrc).toMatch(/uploadedAttachmentIdRef\.current = null;\s*\n\s*uploadedFileRef\.current = null;\s*\n\s*onSuccess\(outcome\.message\);/);
    });
    it('§9.4 — clean-up only from clear-file and Cancelar: no unmount effect, no delayed clean-up, a refused clean-up is "kept" (not an error)', () => {
        expect((correctPoSrc.match(/void cleanupTemporaryPoUpload\(/g) || []).length).toBe(2);
        expect(correctPoSrc).not.toMatch(/return \(\) =>[\s\S]{0,300}cleanupTemporaryPoUpload/);
        expect(correctPoSrc).not.toMatch(/setTimeout\([\s\S]{0,200}cleanupTemporaryPoUpload/);
        const kept = correctPoSrc.slice(correctPoSrc.indexOf('const reportCleanupKept'), correctPoSrc.indexOf('const handleClearFile'));
        expect(kept).toMatch(/level: 'Information'/);
        expect(kept).toMatch(/PO_EVIDENCE_PROTECTED/);
        expect(kept).not.toMatch(/level: 'Warning'|level: 'Error'/);
    });
});

describe('RegisterPoModal — temporary upload clean-up', () => {
    it('cleans up on clear-file and cancel, never after success', () => {
        expect(registerPoSrc).toMatch(/import \{ cleanupTemporaryPoUpload \} from '\.\.\/lib\/poEvidence';/);
        const clear = registerPoSrc.slice(registerPoSrc.indexOf('const handleClearFile = () => {'), registerPoSrc.indexOf('const handleCancel = () => {'));
        expect(clear).toMatch(/cleanupTemporaryPoUpload\(api\.attachments\.delete, uploadedAttachmentIdRef\.current, reportCleanupFailure, reportCleanupKept\)/);
        const cancel = registerPoSrc.slice(registerPoSrc.indexOf('const handleCancel = () => {'), registerPoSrc.indexOf('const handleCancel = () => {') + 600);
        expect(cancel).toMatch(/if \(processing\) return;/);
        expect(cancel).toMatch(/onClose\(\);/);
        expect(registerPoSrc).toMatch(/onClick=\{handleCancel\}\s*\n\s*disabled=\{processing\}/);
        expect(registerPoSrc).toMatch(/uploadedAttachmentIdRef\.current = null;\s*\n\s*uploadedFileRef\.current = null;\s*\n\s*onSuccess\(result\.message/);
        expect(registerPoSrc).not.toMatch(/api\.attachments\.delete\(existingPo/);
    });
    it('§9.4 — clean-up only from clear-file and Cancelar: no unmount effect, no delayed clean-up, a refused clean-up is "kept" (not an error)', () => {
        expect((registerPoSrc.match(/void cleanupTemporaryPoUpload\(/g) || []).length).toBe(2);
        expect(registerPoSrc).not.toMatch(/return \(\) =>[\s\S]{0,300}cleanupTemporaryPoUpload/);
        expect(registerPoSrc).not.toMatch(/setTimeout\([\s\S]{0,200}cleanupTemporaryPoUpload/);
        const kept = registerPoSrc.slice(registerPoSrc.indexOf('const reportCleanupKept'), registerPoSrc.indexOf('const handleClearFile'));
        expect(kept).toMatch(/level: 'Information'/);
        expect(kept).toMatch(/PO_EVIDENCE_PROTECTED/);
    });
});

describe('RequestAttachments — delete offered only for non-evidence P.O. files', () => {
    it('gates the remove button on isPoAttachmentDeleteOffered (server isPoEvidence flag)', () => {
        expect(attachmentsSrc).toMatch(/import \{ isPoAttachmentDeleteOffered \} from '\.\.\/lib\/poEvidence';/);
        expect(attachmentsSrc).toMatch(/\{canEdit && isTypeDeletable\(a\.attachmentTypeCode\) && isPoAttachmentDeleteOffered\(a\) && \(/);
        expect(attachmentsSrc).toContain('isPoEvidence?: boolean | null;');
    });
});

describe('"Menor valor" — per request item through lib/quotationComparison', () => {
    it('RequestQuotations no longer compares totals across documents; uses the shared comparison + lifecycle helpers', () => {
        expect(quotationsSrc).not.toMatch(/lowestByCurrency/);
        expect(quotationsSrc).toMatch(/compareQuotationsPerLine\(quotations\)/);
        expect(quotationsSrc).toMatch(/describeCardComparison\(comparison\.byQuotationId\.get\(q\.id\), q\.currency\)/);
        expect(quotationsSrc).toMatch(/describeItemComparison\(comparison\.byItemId\.get\(item\.id\)\)/);
        expect(quotationsSrc).toMatch(/describeQuotationLifecycle\(q\)/);
        expect(quotationsSrc).toMatch(/describeQuotationItemAdjudication\(item\)/);
        expect(quotationsSrc).toContain('data-testid="quotation-scope-notice"');
        expect(quotationsSrc).toContain('data-testid="quotation-lifecycle-chip"');
        expect(quotationsSrc).toContain('data-testid="quotation-item-adjudication"');
        expect(quotationsSrc).toContain('>SITUAÇÃO</th>');
    });
    it('§9.2 — selection from the card follows the reuse eligibility (describeQuotationSelectability), not the historical label', () => {
        expect(quotationsSrc).toMatch(/const selectability = describeQuotationSelectability\(q\);/);
        expect(quotationsSrc).toMatch(/isFinalApproverMode && isDecisionStage && !q\.isSelected && !isDrawerMode && selectability\.selectable && \(/);
        expect(quotationsSrc).toMatch(/isFinalApproverMode && isDecisionStage && !q\.isSelected && !isDrawerMode && !selectability\.selectable && \(/);
        expect(quotationsSrc).toContain('data-testid="quotation-selection-blocked"');
        expect(quotationsSrc).not.toMatch(/!isDrawerMode && !isHistorical &&/);
    });
    it('BuyerItemsList and the legacy ApprovalDetailPanel path read the same helper; the Area wizard (WizardStepAward) is untouched', () => {
        expect(buyerItemsSrc).not.toMatch(/lowestByCurrency/);
        expect(buyerItemsSrc).toMatch(/const quotationComparison = compareQuotationsPerLine\(group\.quotations\);/);
        expect(buyerItemsSrc).toMatch(/const isLowest = !!quotationComparison\.byQuotationId\.get\(q\.id\)\?\.lowestTotal;/);
        expect(approvalPanelSrc).not.toMatch(/lowestByCurrency/);
        expect(approvalPanelSrc).toMatch(/isLowest=\{!isCandidateBatchContext && !!quotationComparison\.byQuotationId\.get\(q\.id\)\?\.lowestTotal\}/);
        expect(wizardAwardSrc).not.toMatch(/quotationComparison|compareQuotationsPerLine/);
    });
    it('Print View carries the lifecycle "Situação" from the server projection', () => {
        expect(printModelSrc).toMatch(/field\('Situação', quotationSituation\(q\)\)/);
        expect(printModelSrc).toMatch(/describeQuotationLifecycle\(q\)/);
    });
});
