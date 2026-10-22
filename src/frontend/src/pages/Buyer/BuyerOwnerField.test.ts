import { describe, it, expect } from 'vitest';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { BuyerOwnerField } from './BuyerOwnerField';
import { resolveAssignAction } from './workspaceAssignActions';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';

/**
 * Rendered-component regression for the invisible takeover button (TEST v2.248.0, REQ-271): the button was rendered
 * inside a `white-space: nowrap; overflow: hidden` value container and clipped. The vitest environment is `node`
 * (no DOM runtime), so the component is rendered with react-dom/server and the resulting HTML is inspected for the
 * structural property that caused the defect: the button must never be a descendant of a nowrap/overflow-hidden box.
 */
const ROLE_NAMES = { buyer: 'Buyer', systemAdministrator: 'System Administrator', localManager: 'Local Manager' };
const render = (kind: 'CLAIM' | 'TAKEOVER' | 'NONE', buyerName: string | null = 'Samuel Mpoco', busy = false) => {
    const decision = kind === 'TAKEOVER'
        ? resolveAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: 'u-samuel' }, { id: 'u-celestina', roles: ['Requester', 'Buyer'] }, ROLE_NAMES)
        : kind === 'CLAIM'
            ? resolveAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: null }, { id: 'u-celestina', roles: ['Buyer'] }, ROLE_NAMES)
            : resolveAssignAction({ requestTypeCode: 'PAYMENT', requestStatusCode: 'WAITING_QUOTATION', buyerId: 'u-samuel' }, { id: 'u-celestina', roles: ['Buyer'] }, ROLE_NAMES);
    expect(decision.kind).toBe(kind);
    return renderToStaticMarkup(React.createElement(BuyerOwnerField, { buyerName, assignAction: decision, busy, onClaim: () => {}, onTakeover: () => {} }));
};

/** Returns the chain of opening tags enclosing the first element with the given attribute, outermost first (simple, non-nested-tag-aware walker sufficient for this markup). */
function ancestorsOf(html: string, marker: string): string[] {
    const idx = html.indexOf(marker);
    expect(idx).toBeGreaterThan(-1);
    const before = html.slice(0, idx);
    const stack: string[] = [];
    const tagRe = /<(\/?)([a-z][a-z0-9]*)([^>]*)>/gi;
    let m: RegExpExecArray | null;
    while ((m = tagRe.exec(before)) !== null) {
        const [full, close, name, attrs] = m;
        if (name === 'svg' || name === 'path' || name === 'circle' || name === 'line') continue;
        if (close) { stack.pop(); continue; }
        if (attrs.endsWith('/')) continue;   // self-closing
        stack.push(full);
    }
    return stack;
}

describe('BuyerOwnerField — the action is never clipped by the value container', () => {
    it('TAKEOVER (Celestina, Buyer, request owned by Samuel): the button is rendered and no ancestor hides overflow or forbids wrapping', () => {
        const html = render('TAKEOVER');
        expect(html).toContain('data-tour="buyer-workspace-assign"');
        expect(html).toContain('Assumir pedido de outro comprador');
        const ancestors = ancestorsOf(html, 'data-tour="buyer-workspace-assign"');
        expect(ancestors.length).toBeGreaterThan(0);
        for (const tag of ancestors) {
            expect(tag).not.toMatch(/overflow:\s*hidden/);
            expect(tag).not.toMatch(/white-space:\s*nowrap/);
            expect(tag).not.toMatch(/text-overflow:\s*ellipsis/);
        }
        // The owner's name keeps the ellipsis treatment in its OWN box, which must not contain the button.
        const nameBox = html.match(/<div style="[^"]*text-overflow:ellipsis[^"]*"[^>]*>([^<]*)<\/div>/);
        expect(nameBox).not.toBeNull();
        expect(nameBox![1]).toBe('Samuel Mpoco');
    });
    it('CLAIM renders the claim label; NONE renders no button; busy disables it', () => {
        expect(render('CLAIM', null)).toContain('>Não atribuído<');
        expect(render('CLAIM', null)).toContain('Assumir pedido<');
        expect(render('NONE')).not.toContain('data-tour="buyer-workspace-assign"');
        expect(render('TAKEOVER', 'Samuel Mpoco', true)).toMatch(/<button[^>]*disabled=""/);
    });
    it('the Workspace header uses BuyerOwnerField for "Comprador" and no longer nests the button in Meta', () => {
        expect(workspaceSrc).toMatch(/<BuyerOwnerField\s/);
        expect(workspaceSrc).toMatch(/assignAction=\{assignAction\}/);
        expect(workspaceSrc).not.toMatch(/<Meta[^>]*label="Comprador"/);
    });
});
