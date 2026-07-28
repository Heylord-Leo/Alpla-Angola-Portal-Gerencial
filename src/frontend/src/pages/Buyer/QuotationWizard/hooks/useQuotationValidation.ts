import { OcrDraft, OcrDraftItem } from '../../../../types';
import { validateReconciliationJustification } from '../../../../lib/reconciliationJustificationValidator';
import { hasMaterialOcrChange, isFractionalForIntegerUnit } from '../../../../lib/lineReconciliation';

export interface QuotationValidationResult {
    isValid: boolean;
    errors: string[];
}

/** Quality-validates a justification, mirroring the backend. An untouched legacy line — status
 * and justification exactly as persisted (tracked via originalReconciliationJustification, set at
 * hydration in BuyerItemsList) — is exempt, mirroring the backend's legacy-vs-edited skip. */
function isJustificationSatisfied(item: OcrDraftItem): boolean {
    const isLegacyUntouched = item.originalReconciliationJustification != null
        && (item.reconciliationJustification ?? '') === (item.originalReconciliationJustification ?? '');
    if (isLegacyUntouched) return true;
    return validateReconciliationJustification(item.reconciliationJustification).isValid;
}

export function useQuotationValidation() {
    
    const validateDraft = (draft: OcrDraft | null, ivaRates: any[] = [], units: any[] = []): QuotationValidationResult => {
        const errors: string[] = [];
        
        if (!draft) {
            return { isValid: false, errors: ['Nenhum dado de cotação encontrado.'] };
        }

        if (!draft.supplierId) {
            errors.push('Fornecedor é obrigatório.');
        }

        if (!draft.documentNumber || draft.documentNumber.trim() === '') {
            errors.push('Número do documento é obrigatório.');
        }

        if (!draft.documentDate) {
            errors.push('Data do documento é obrigatória.');
        }

        if (!draft.dueDate) {
            errors.push('Data de vencimento da cotação é obrigatória.');
        }

        if (!draft.documentType) {
            errors.push('Tipo de documento da cotação é obrigatório.');
        }

        if (!draft.currency) {
            errors.push('Moeda é obrigatória.');
        }

        if (draft.items.length === 0) {
            errors.push('A cotação deve ter pelo menos um item.');
        }

        // We can add advanced mapping validation here in Phase 3
        const realItems = draft.items.filter(i => i.reconciliationStatus !== 'NOT_QUOTED');
        const notQuotedItems = draft.items.filter(i => i.reconciliationStatus === 'NOT_QUOTED');

        // NOT_QUOTED is document-scoped ("this supplier didn't quote this item"):
        // no justification required — that belongs to the separate "Encerrar sem
        // cotação" flow. Only the zeroed-values invariant is enforced.
        for (const item of notQuotedItems) {
            if ((item.quantity && item.quantity > 0) || (item.unitPrice && item.unitPrice > 0) || (item.discountAmount && item.discountAmount > 0)) {
                errors.push(`Item ${item.lineNumber}: Itens não cotados nesta cotação devem ter quantidade e valores financeiros zerados.`);
            }
        }

        for (const item of realItems) {
            if (!item.reconciliationStatus) {
                errors.push(`Item ${item.lineNumber}: Status de reconciliação ausente.`);
            } else if (item.reconciliationStatus === 'SUBSTITUTE' && (!item.reconciliationJustification || !item.reconciliationJustification.trim())) {
                errors.push(`Item ${item.lineNumber}: Justificativa é obrigatória para itens substitutos.`);
            } else if (item.reconciliationStatus === 'EXTRA_ITEM' && !isJustificationSatisfied(item)) {
                errors.push(`Item ${item.lineNumber}: Justificativa é obrigatória para itens adicionais.`);
            } else if (item.reconciliationStatus === 'IGNORED' && (item.totalPrice ?? 0) > 0 && !isJustificationSatisfied(item)) {
                // Ignored line WITH value leaves the integrity baseline — requires its own justification.
                errors.push(`Item ${item.lineNumber}: Justificativa é obrigatória para ignorar uma linha com valor.`);
            } else if (item.reconciliationStatus === 'MAPPED' && !item.mappedRequestLineItemId) {
                errors.push(`Item ${item.lineNumber}: Deve selecionar o item solicitado correspondente.`);
            } else if (item.reconciliationStatus === 'SUBSTITUTE' && !item.mappedRequestLineItemId) {
                errors.push(`Item ${item.lineNumber}: Deve selecionar o item solicitado correspondente para substituição.`);
            }

            // Integer-unit quantities must be whole numbers (mirrors the backend invalid-input rule).
            if (isFractionalForIntegerUnit(item, units)) {
                errors.push(`Item ${item.lineNumber}: a unidade não permite quantidade fracionada; informe um número inteiro.`);
            }

            // Material financial edit vs the OCR baseline requires a consolidated line-adjustment reason
            // (distinct from the reconciliation justification). Mirrors the backend's 400 gate.
            if (['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM'].includes(item.reconciliationStatus as string)
                && hasMaterialOcrChange(item, ivaRates)
                && !validateReconciliationJustification(item.lineAdjustmentJustification).isValid) {
                errors.push(`Item ${item.lineNumber}: informe o motivo das alterações desta linha em relação ao documento OCR.`);
            }
        }

        return {
            isValid: errors.length === 0,
            errors
        };
    };

    return {
        validateDraft
    };
}
