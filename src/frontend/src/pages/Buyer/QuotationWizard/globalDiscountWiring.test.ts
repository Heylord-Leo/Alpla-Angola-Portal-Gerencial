import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards for the v2.249.6 global-discount handling (REQ-21/10/2026-595). No RTL.
import documentsStep from './WizardStepDocumentsOcr.tsx?raw';
import finalReview from './WizardStepFinalReview.tsx?raw';
import stateHook from './hooks/useQuotationWizardState.ts?raw';
import processor from '../../../hooks/useOcrProcessor.ts?raw';
import batchModal from '../PartialApprovalBatchModal.tsx?raw';
import eligibility from '../batchEligibility.ts?raw';
import requestCards from '../../Requests/components/RequestQuotations.tsx?raw';
import buyerList from '../BuyerItemsList.tsx?raw';
import pricingLib from '../../../lib/quotationPricing.ts?raw';

describe('OCR draft building — discounts are resolved by the shared rule, never silently rewritten', () => {
    it('useOcrProcessor uses resolveOcrLineDiscount and flags uncertain lines instead of the old Strategy A overwrite', () => {
        expect(processor).toMatch(/resolveOcrLineDiscount\(\{/);
        expect(processor).toMatch(/discountUncertain: resolvedLine\.discountUncertain,/);
        expect(processor).toMatch(/ocrImpliedDiscount: resolvedLine\.impliedDiscount/);
        expect(processor).not.toMatch(/Strategy A: Use totalPrice as anchor/);
        expect(processor).not.toMatch(/resolvedDiscount = Math\.max\(0, impliedDiscount\);/);
    });

    it('the header "Desconto Comercial" is resolved against the line discounts (aggregate vs document-level) before the totals', () => {
        expect(processor).toMatch(/resolveOcrHeaderDiscount\(draft\.discountAmount \|\| 0, draft\.items\.map\(i => i\.discountAmount \|\| 0\)\)/);
        expect(processor).toMatch(/draft\.globalDiscountKind = headerDiscount\.kind;/);
        expect(processor).toMatch(/draft\.discountAmount = headerDiscount\.globalDiscount;/);
        // …and before the declared-total reconciliation that follows.
        expect(processor.indexOf('resolveOcrHeaderDiscount(draft.discountAmount')).toBeLessThan(processor.indexOf('draft.totalAmount = reconcileDraftTotal(draft.totalAmount'));
    });
});

describe('Document step — confirmation of uncertain discounts and the explicit allocation', () => {
    it('shows the per-line confirmation marker and blocks the step until confirmed (like uncertain IVA)', () => {
        expect(documentsStep).toMatch(/data-testid="discount-uncertain"/);
        expect(documentsStep).toMatch(/data-testid="discount-uncertain-banner"/);
        expect(stateHook).toMatch(/const noUncertainDiscount = draft\.items\.every\(i => !i\.discountUncertain\);/);
        expect(stateHook).toMatch(/&& noUncertainIva && noUncertainDiscount && allItemsValid && classificationSettled;/);
        expect(stateHook).toMatch(/if \(field === 'discountAmount'\) item\.discountUncertain = false;/);
    });

    it('a global discount shows the warning and the explicit "Distribuir pelas linhas" action, never an automatic redistribution', () => {
        expect(documentsStep).toMatch(/data-testid="global-discount-unallocated"/);
        expect(documentsStep).toMatch(/data-testid="distribute-global-discount"/);
        expect(documentsStep).toMatch(/onClick=\{\(\) => distributeGlobalDiscountToLines\(ivaRates\)\}/);
        expect(documentsStep).toMatch(/não pode ser enviada para aprovação/);
        expect(documentsStep).toMatch(/data-testid="global-discount-note"/);
        // The hook action delegates to the pure draft transform of the shared lib (tested behaviourally in quotationPricing.test.ts:
        // repeat press never applies twice, excess refused, ignored / zero lines untouched); no useEffect auto-run.
        expect(stateHook).toMatch(/const outcome = applyGlobalDiscountAllocationToDraft\(prev, ivaRates\);/);
        expect(stateHook).toMatch(/return outcome\.draft;/);
        expect(stateHook).not.toMatch(/useEffect\([^)]*distributeGlobalDiscountToLines/);
        expect(pricingLib).toMatch(/const next = \{ \.\.\.draft, items, discountAmount: 0, globalDiscountKind: 'NONE' as const/);
        // The running draft total and the allocation share ONE arithmetic (the hook's recalculateQuotationTotal is the lib function).
        expect(stateHook).toMatch(/const recalculateQuotationTotal = \(d: OcrDraft, rates: any\[\]\) => computeDraftQuotationTotal\(d, rates\);/);
        // The step says when NOT to distribute: printed per-line discounts are entered per line.
        expect(documentsStep).toMatch(/Se o documento imprime descontos por linha \(valor por artigo\), não use esta distribuição:/);
        expect(documentsStep).toMatch(/é recusada se exceder o valor líquido das linhas/);
    });

    it('the final review warns that an unallocated global discount blocks submission', () => {
        expect(finalReview).toMatch(/if \(\(draft\.discountAmount \|\| 0\) > 0\) \{/);
        expect(finalReview).toMatch(/não poderá ser enviada para aprovação até o desconto estar refletido nas linhas/);
    });
});

describe('Submission surfaces — an unresolved quotation is never offered and the 409 is explained', () => {
    it('batchEligibility exposes the line-resolved rule from the shared lib', () => {
        expect(eligibility).toMatch(/export function isQuotationPricingLineResolved\(quotation: any\): boolean \{\s*return evaluateQuotationLinePricing\(quotation \?\? \{\}\)\.isLineResolved;/);
    });

    it('PartialApprovalBatchModal skips the lines of an unresolved quotation, explains it under the pending line and reads the 409', () => {
        expect(batchModal).toMatch(/if \(mapped && !isQuotationPricingLineResolved\(quotation\)\) \{ pricingBlockedByItem\[reqItemId\]/);
        expect(batchModal).toMatch(/data-testid="pending-pricing-hint"/);
        expect(batchModal).toMatch(/Distribuir pelas linhas/);
        expect(batchModal).toMatch(/const pricingConflict = readLinePricingConflict\(err\);/);
        expect(batchModal).toMatch(/setPendingPricingBlocked\(pricingBlockedByItem\);/);
    });

    it('both saved-quotation card surfaces show the "Desconto global não distribuído" chip from the shared description', () => {
        expect(requestCards).toMatch(/describeQuotationPricingState\(q, q\.currency\)/);
        expect(requestCards).toMatch(/data-testid="quotation-pricing-chip"/);
        expect(buyerList).toMatch(/describeQuotationPricingState\(q, q\.currency\)/);
        expect(buyerList).toMatch(/data-testid="quotation-pricing-chip"/);
    });
});
