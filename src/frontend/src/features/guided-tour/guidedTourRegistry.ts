import type { TourId, TourDefinition } from './guidedTourTypes';
import { PORTAL_MAIN_STEPS } from './tours/portalMainTour';
import { PURCHASING_LOGISTICS_STEPS } from './tours/purchasingLogisticsTour';
import { REQUESTS_PAGE_STEPS } from './tours/requestsPageTour';
import { BUYER_ITEMS_PAGE_STEPS } from './tours/buyerItemsPageTour';
import { RECEIVING_WORKSPACE_STEPS } from './tours/receivingWorkspaceTour';
import { APPROVALS_CENTER_STEPS } from './tours/approvalsCenterTour';
import { APPROVAL_DRAWER_AREA_STEPS } from './tours/approvalDrawerAreaTour';
import { APPROVAL_DRAWER_FINAL_STEPS } from './tours/approvalDrawerFinalTour';

/**
 * Guided Tour Registry
 * 
 * Central registry mapping TourId → TourDefinition.
 * Route matching uses `startsWith` against the current pathname.
 * 
 * Tour levels:
 * - portal: always available, auto-shows on first access
 * - module: available when the user is inside a module's routes
 * - page: available when the user is on a specific page route
 */
export const TOUR_REGISTRY: TourDefinition[] = [
    {
        id: 'portal-main',
        level: 'portal',
        label: 'Tour inicial do Portal',
        routes: ['/'],  // Always available (matched as catch-all)
        steps: PORTAL_MAIN_STEPS,
        autoShow: true,
    },
    {
        id: 'module-purchasing-logistics',
        level: 'module',
        label: 'Tour deste módulo',
        routes: ['/purchasing', '/requests', '/buyer/items', '/receiving'],
        steps: PURCHASING_LOGISTICS_STEPS,
    },
    {
        id: 'page-requests',
        level: 'page',
        label: 'Tour desta tela',
        routes: ['/requests'],
        steps: REQUESTS_PAGE_STEPS,
    },
    {
        id: 'page-buyer-items',
        level: 'page',
        label: 'Tour desta tela',
        routes: ['/buyer/items'],
        steps: BUYER_ITEMS_PAGE_STEPS,
    },
    {
        id: 'page-receiving-workspace',
        level: 'page',
        label: 'Tour desta tela',
        routes: ['/receiving/workspace', '/receiving'],
        steps: RECEIVING_WORKSPACE_STEPS,
    },
    {
        id: 'page-approvals-center',
        level: 'page',
        label: 'Tour desta tela',
        routes: ['/approvals'],
        steps: APPROVALS_CENTER_STEPS,
    },
    {
        id: 'drawer-approval-area',
        level: 'drawer',
        label: 'Tour — Aprovação de Área',
        routes: [],  // Drawer tours are started manually, no route matching
        steps: APPROVAL_DRAWER_AREA_STEPS,
        scrollContainerSelector: '[data-tour-scroll-container="approval-drawer"]',
    },
    {
        id: 'drawer-approval-final',
        level: 'drawer',
        label: 'Tour — Aprovação Final',
        routes: [],  // Drawer tours are started manually, no route matching
        steps: APPROVAL_DRAWER_FINAL_STEPS,
        scrollContainerSelector: '[data-tour-scroll-container="approval-drawer"]',
    },
];

/** Look up a tour definition by its ID */
export function getTourById(id: TourId): TourDefinition | undefined {
    return TOUR_REGISTRY.find(t => t.id === id);
}

/** Get the portal-level tour (always available) */
export function getPortalTour(): TourDefinition {
    return TOUR_REGISTRY.find(t => t.id === 'portal-main')!;
}

/**
 * Resolve the module and page tours available for a given route.
 * Uses `startsWith` matching against each tour's route prefixes.
 * 
 * For page-level, prefers the most specific (longest) route match.
 * For module-level, returns the first module-level tour whose routes match.
 */
export function getToursForRoute(pathname: string): {
    portal: TourDefinition;
    module?: TourDefinition;
    page?: TourDefinition;
} {
    const portal = getPortalTour();

    // Find matching module tour
    const module = TOUR_REGISTRY.find(
        t => t.level === 'module' && t.routes.some(r => pathname.startsWith(r))
    );

    // Find matching page tour — prefer the most specific route (longest prefix)
    const pageMatches = TOUR_REGISTRY
        .filter(t => t.level === 'page' && t.routes.some(r => pathname.startsWith(r)))
        .sort((a, b) => {
            const longestA = Math.max(...a.routes.filter(r => pathname.startsWith(r)).map(r => r.length));
            const longestB = Math.max(...b.routes.filter(r => pathname.startsWith(r)).map(r => r.length));
            return longestB - longestA;
        });
    const page = pageMatches[0];

    return { portal, module, page };
}
