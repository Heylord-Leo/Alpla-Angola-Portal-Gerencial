import type { LiveGuideId } from './liveGuideTypes';

/**
 * Live Guide Registry
 *
 * Central registry for all live guide definitions.
 * Live guides are registered with their route and metadata,
 * but the actual step definitions are created lazily via factory
 * functions (because they need form state getters).
 *
 * This registry stores only metadata entries for route matching
 * and availability checks. The full guide definition (with steps)
 * is created at runtime when the guide is started.
 */

/** Metadata entry for registry — steps are not included here */
export interface LiveGuideRegistryEntry {
    id: LiveGuideId;
    type: 'live-guide';
    module: string;
    route: string;
    title: string;
    description: string;
    enabled: boolean;
}

/**
 * Static registry of available live guides.
 * Add new entries here when creating new live guides.
 */
export const LIVE_GUIDE_REGISTRY: LiveGuideRegistryEntry[] = [
    {
        id: 'request-creation-live-guide',
        type: 'live-guide',
        module: 'requests',
        route: '/requests/new',
        title: 'Guia — Criar Pedido',
        description: 'Ajuda passo a passo para criar um novo pedido.',
        enabled: true,
    },
];

/** Look up a registry entry by its ID */
export function getLiveGuideEntryById(id: LiveGuideId): LiveGuideRegistryEntry | undefined {
    return LIVE_GUIDE_REGISTRY.find(g => g.id === id);
}

/**
 * Get live guide entries available for a given route.
 * Uses `startsWith` matching against each guide's route.
 * Only returns enabled guides.
 */
export function getLiveGuidesForRoute(pathname: string): LiveGuideRegistryEntry[] {
    return LIVE_GUIDE_REGISTRY.filter(
        g => g.enabled && pathname.startsWith(g.route)
    );
}
