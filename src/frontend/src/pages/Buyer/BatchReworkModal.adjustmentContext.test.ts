import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards, following the
// BuyerRequestWorkspace.wizardMount.test.ts pattern.
import modalSrc from './BatchReworkModal.tsx?raw';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import classicSrc from './BuyerItemsList.tsx?raw';

// Adjustment V2 Phase 1 quick fixes. The load-bearing regression: "Motivo do reajuste" previously
// rendered batch.comment — the BUYER's own batch text — as if it were the Approver's adjustment
// reason. QF1/QF3 replace that with the backend-derived adjustment context; QF2/QF5 fix the labels
// and copy; QF4 bridges to the existing quotation tools.

describe('QF1/QF3 — the modal shows the APPROVER adjustment context, never batch.comment as motive', () => {
    it('renders the backend-derived adjustment fields', () => {
        expect(modalSrc).toMatch(/batch\.adjustmentReason/);
        expect(modalSrc).toMatch(/batch\.adjustmentRequestedByName/);
        expect(modalSrc).toMatch(/batch\.adjustmentRequestedAtUtc/);
        expect(modalSrc).toMatch(/batch\.adjustmentSourceStage/);
    });

    it('never labels batch.comment as "Motivo do reajuste" (the exact regression)', () => {
        expect(modalSrc).not.toMatch(/Motivo do reajuste:<\/strong>\s*\{batch\.comment\}/);
    });

    it('batch.comment, when rendered, is explicitly the Buyer comment', () => {
        expect(modalSrc).toMatch(/Comentário do lote \(Comprador\):<\/strong>\s*\{batch\.comment\}/);
    });

    it('context header shows source stage with both stages and neutral fallbacks', () => {
        expect(modalSrc).toMatch(/Aprovação Final/);
        expect(modalSrc).toMatch(/Aprovação de Área/);
        expect(modalSrc).toMatch(/Solicitado por:/);
        expect(modalSrc).toMatch(/Solicitado em:/);
        expect(modalSrc).toMatch(/Informação não disponível/);
    });
});

describe('QF2/QF5 — honest labels and explanatory copy', () => {
    it('modal title is "Revisar Lote #N para Reenvio" — "Corrigir Lote" is gone', () => {
        expect(modalSrc).toMatch(/Revisar Lote #\{batch\.batchNumber\} para Reenvio/);
        expect(modalSrc).not.toMatch(/Corrigir Lote/);
    });

    it('save button is "Salvar Composição e Reenviar" — old label absent', () => {
        expect(modalSrc).toMatch(/Salvar Composição e Reenviar/);
        expect(modalSrc).not.toMatch(/Salvar Correções e Reenviar/);
    });

    it('classic batch card uses the new label', () => {
        expect(classicSrc).toMatch(/Revisar Lote para Reenvio/);
        expect(classicSrc).not.toMatch(/Corrigir Lote\b/);
    });

    it('explanatory copy states the composition-only limitation and points to Gerenciar Cotações', () => {
        expect(modalSrc).toMatch(/Esta tela permite revisar a composição do lote e as opções de cotação\./);
        expect(modalSrc).toMatch(/Para alterar preços, fornecedor ou documentos, utilize Gerenciar Cotações\./);
    });
});

describe('QF4 — quotation bridge', () => {
    it('modal exposes the "Gerenciar Cotações" action via the host-provided callback', () => {
        expect(modalSrc).toMatch(/onManageQuotations/);
        expect(modalSrc).toMatch(/Gerenciar Cotações/);
    });

    it('both hosts provide the bridge (Workspace → quotes tab; classic → close onto the quotation screen)', () => {
        expect(workspaceSrc).toMatch(/onManageQuotations=\{\(\) => \{ setActiveHost\(null\); setTab\('quotes'\); \}\}/);
        expect(classicSrc).toMatch(/onManageQuotations=\{\(\) => setBatchReworkModal\(/);
    });

    it('Workspace renders the quotation wizard entries during RESOLVE_ADJUSTMENT', () => {
        const resolveBlock = workspaceSrc.split("actionableCode === 'RESOLVE_ADJUSTMENT'")[1]?.split('actionableCode ===')[0] ?? '';
        expect(resolveBlock).toMatch(/openAddQuotation\(ws\.requestId, 'UPLOAD'\)/);
        expect(resolveBlock).toMatch(/openAddQuotation\(ws\.requestId, 'MANUAL'\)/);
    });

    it('normal ADD_QUOTATION wizard entries remain intact', () => {
        const addBlock = workspaceSrc.split("actionableCode === 'ADD_QUOTATION'")[1] ?? '';
        expect(addBlock).toMatch(/openAddQuotation\(ws\.requestId, 'UPLOAD'\)/);
        expect(addBlock).toMatch(/openAddQuotation\(ws\.requestId, 'MANUAL'\)/);
    });
});
