import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import src from './PartialApprovalBatchModal.tsx?raw';

// v2.249.2 — the submission modal applies the SAME candidate rules as the server (CreateBatch): a reuse-blocked
// candidate (released/cancelled batch item, no authorization) is never offered, the line stays "Pendente de cotação"
// and the Buyer is told why (explicit reuse authorization in the classic tools, or a new quotation).

describe('PartialApprovalBatchModal — reuse-blocked candidates', () => {
    it('excludes reuse-blocked candidates before the batch-hold check', () => {
        expect(src).toMatch(/import \{ isQuotationItemSelectableForApproval, isQuotationItemReuseBlocked \} from '\.\/batchEligibility'/);
        expect(src).toMatch(/if \(mapped && isQuotationItemReuseBlocked\(qi\)\) \{ reuseBlockedCount\+\+; return; \}/);
        expect(src).toMatch(/if \(mapped && isQuotationItemSelectableForApproval\(qi\.id, group\)\) \{/);
    });
    it('keeps the line pending and records the reuse hint per line', () => {
        expect(src).toMatch(/newPendingItems\.push\(normalizedReqItem\);\s*if \(reuseBlockedCount > 0 && reqItemId\) newPendingReuseBlocked\[String\(reqItemId\)\] = reuseBlockedCount;/);
        expect(src).toMatch(/setPendingReuseBlocked\(newPendingReuseBlocked\)/);
    });
    it('renders the hint next to "Pendente de cotação" naming both ways out (authorization or a new quotation)', () => {
        expect(src).toMatch(/data-testid="pending-reuse-hint"/);
        expect(src).toContain('exige(m) autorização de reutilização (ferramentas clássicas → Reutilizar cotação) ou registe uma nova cotação.');
        expect(src).toContain('Pendente de cotação');
    });
});
