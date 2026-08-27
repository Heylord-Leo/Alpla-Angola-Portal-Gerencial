import { describe, it, expect } from 'vitest';
// Vite `?raw` imports (typed as string via vite/client) — no Node builtins, so this typechecks under
// the browser tsconfig. This repo's frontend vitest runs in `environment: 'node'` with NO jsdom /
// Testing Library (component-render tests are intentionally out of scope — see vitest.config.ts), so a
// mount/unmount-count component test would require adding jsdom + RTL, a broad infra change that is out
// of scope here. This is the minimal seam: a SOURCE-LEVEL structural guard on the host's JSX.
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import classicSrc from './BuyerItemsList.tsx?raw';

// Regression guard for the Buyer Workspace quotation-wizard MOUNT STABILITY bug (REQ-24/08/2026-293).
//
// Root cause (verified): BuyerRequestWorkspace conditionally mounted the shared QuotationWizardModal
// with a host-level `{wizardHost.wizardState.isOpen && ( <QuotationWizardModal ... /> )}`. The modal is
// designed to stay ALWAYS mounted and self-gate on `wizardState.isOpen` internally (it owns its own
// `mounted` gate, `if (!isOpen) return null`, portal, and body-scroll-lock lifecycle). Gating at the
// host remounts the modal on every open → mount flash + scroll-lock leak + a disrupted OCR flow that
// surfaced the generic "Erro ao processar documento via OCR." banner. The CLASSIC host (BuyerItemsList)
// renders the SAME modal UNCONDITIONALLY and works correctly in PROD. This test locks that contract.

// Matches the exact regression: an `.isOpen &&` conditional immediately wrapping <QuotationWizardModal
// (tolerant of whitespace/newlines and an intervening `(`), regardless of the isOpen owner prefix.
const ISOPEN_GATED_MODAL = /\.isOpen\s*&&\s*\(?\s*<QuotationWizardModal\b/;

describe('Buyer Workspace quotation-wizard mount stability', () => {
    it('BuyerRequestWorkspace renders <QuotationWizardModal> (the modal is present)', () => {
        expect(workspaceSrc).toMatch(/<QuotationWizardModal\b/);
    });

    it('BuyerRequestWorkspace mounts the modal UNCONDITIONALLY — no host-level isOpen && gate (regression guard)', () => {
        // The bug was `{wizardHost.wizardState.isOpen && ( <QuotationWizardModal ... /> )}`.
        // The modal must be rendered directly, letting it self-gate on isOpen internally.
        expect(workspaceSrc).not.toMatch(ISOPEN_GATED_MODAL);
    });

    it('CLASSIC BuyerItemsList (known-good baseline) also renders the modal unconditionally', () => {
        expect(classicSrc).toMatch(/<QuotationWizardModal\b/);
        expect(classicSrc).not.toMatch(ISOPEN_GATED_MODAL);
    });

    it('both hosts share the exact same always-mounted contract for the shared modal', () => {
        // Neither host may gate the shared modal's mount on isOpen — visibility is the modal's concern.
        expect(ISOPEN_GATED_MODAL.test(workspaceSrc)).toBe(false);
        expect(ISOPEN_GATED_MODAL.test(classicSrc)).toBe(false);
    });
});
